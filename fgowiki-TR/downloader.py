# -*- coding: utf-8 -*-
"""
FGO Wiki 资源下载器模块
负责下载和管理下载的文件
"""

import os
import re
import time
import logging
from typing import List, Dict, Optional, Callable
from pathlib import Path
from urllib.parse import urlparse, unquote, quote
from concurrent.futures import ThreadPoolExecutor, as_completed
import requests
from tqdm import tqdm

import config

logger = logging.getLogger(__name__)


class Downloader:
    """FGO Wiki资源下载器"""

    def __init__(self, max_workers: int = None):
        self.max_workers = max_workers or config.MAX_WORKERS
        self.session = requests.Session()
        self.session.headers.update(config.HEADERS)
        self.download_root = Path(config.DOWNLOAD_ROOT)
        self.downloaded_count = 0
        self.failed_count = 0
        self.skipped_count = 0

    def _sanitize_filename(self, filename: str) -> str:
        """
        清理文件名，移除非法字符
        :param filename: 原始文件名
        :return: 清理后的文件名
        """
        illegal_chars = r'[<>:"/\\|?*\x00-\x1f]'
        filename = re.sub(illegal_chars, '_', filename)
        filename = filename.strip('. ')
        if not filename:
            filename = 'unnamed'
        return filename[:200]

    def _get_filename_from_url(self, url: str) -> str:
        """
        从URL中提取文件名
        :param url: 文件URL
        :return: 文件名
        """
        parsed = urlparse(url)
        path = unquote(parsed.path)
        filename = os.path.basename(path)

        if not filename or '.' not in filename:
            ext = self._get_extension_from_url(url)
            filename = f'file{ext}'

        return self._sanitize_filename(filename)

    def _get_extension_from_url(self, url: str) -> str:
        """
        从URL判断文件扩展名
        :param url: 文件URL
        :return: 文件扩展名
        """
        if '.png' in url:
            return '.png'
        elif '.jpg' in url or '.jpeg' in url:
            return '.jpg'
        elif '.gif' in url:
            return '.gif'
        elif '.webp' in url:
            return '.webp'
        elif '.mp3' in url or '.ogg' in url:
            return '.mp3'
        elif '.webm' in url:
            return '.webm'
        else:
            return '.png'

    def _ensure_directory(self, path: Path) -> None:
        """
        确保目录存在
        :param path: 目录路径
        """
        path.mkdir(parents=True, exist_ok=True)

    def _get_file_save_path(self, base_dir: Path, filename: str) -> Path:
        """
        获取文件保存路径，如果文件已存在则添加数字后缀
        :param base_dir: 基础目录
        :param filename: 文件名
        :return: 保存路径
        """
        file_path = base_dir / filename
        if not file_path.exists():
            return file_path

        name, ext = os.path.splitext(filename)
        counter = 1
        while True:
            new_filename = f"{name}_{counter}{ext}"
            new_path = base_dir / new_filename
            if not new_path.exists():
                return new_path
            counter += 1

    def download_file(self, url: str, save_path: Path,
                     progress_callback: Callable[[int, int], None] = None) -> bool:
        """
        下载单个文件
        :param url: 文件URL
        :param save_path: 保存路径
        :param progress_callback: 进度回调函数
        :return: 下载是否成功
        """
        for attempt in range(config.MAX_RETRIES):
            try:
                response = self.session.get(url, stream=True, timeout=config.REQUEST_TIMEOUT)
                response.raise_for_status()

                total_size = int(response.headers.get('content-length', 0))
                self._ensure_directory(save_path.parent)

                with open(save_path, 'wb') as f:
                    downloaded = 0
                    for chunk in response.iter_content(chunk_size=8192):
                        if chunk:
                            f.write(chunk)
                            downloaded += len(chunk)
                            if progress_callback and total_size:
                                progress_callback(downloaded, total_size)

                return True

            except requests.RequestException as e:
                logger.warning(f"下载失败 (尝试 {attempt + 1}/{config.MAX_RETRIES}): {url} - {e}")
                if attempt < config.MAX_RETRIES - 1:
                    time.sleep(config.REQUEST_DELAY * (attempt + 1))
                else:
                    logger.error(f"下载最终失败: {url}")
                    return False

        return False

    def download_servant(self, servant_info: Dict[str, str],
                        scraper,
                        categories: List[str] = None) -> bool:
        """
        下载英灵的所有资源
        :param servant_info: 英灵信息字典
        :param scraper: WikiScraper实例
        :param categories: 要下载的资源类别
        :return: 下载是否成功
        """
        servant_name = servant_info['name']
        servant_url = servant_info['url']

        logger.info(f"开始下载英灵: {servant_name}")

        servant_dir = self.download_root / "英灵" / self._sanitize_filename(servant_name)
        self._ensure_directory(servant_dir)

        if categories is None:
            categories = list(config.SERVANT_DIR_STRUCTURE.keys())

        try:
            details = scraper.get_servant_details(servant_url)
            if not details:
                logger.warning(f"无法获取英灵详情: {servant_name}")
                return False

            success = True

            if 'portrait' in categories and details.get('images'):
                portrait_dir = servant_dir / config.SERVANT_DIR_STRUCTURE['portrait']
                self._ensure_directory(portrait_dir)

                for img_url in details['images']:
                    filename = self._get_filename_from_url(img_url)
                    save_path = self._get_file_save_path(portrait_dir, filename)

                    if save_path.exists():
                        logger.debug(f"跳过已存在文件: {save_path}")
                        self.skipped_count += 1
                        continue

                    if self.download_file(img_url, save_path):
                        self.downloaded_count += 1
                        logger.info(f"已下载: {save_path.name}")
                    else:
                        self.failed_count += 1
                        success = False

            if 'description' in categories and details.get('descriptions'):
                desc_dir = servant_dir / config.SERVANT_DIR_STRUCTURE['description']
                self._ensure_directory(desc_dir)

                desc_file = desc_dir / '介绍.txt'
                with open(desc_file, 'w', encoding='utf-8') as f:
                    f.write(f"英灵名称: {servant_name}\n")
                    f.write(f"资料来源: {servant_url}\n\n")
                    for key, value in details['descriptions'].items():
                        f.write(f"【{key}】\n{value}\n\n")

                logger.info(f"已保存文字介绍: {desc_file.name}")

            if 'voice' in categories and details.get('voice_urls'):
                voice_dir = servant_dir / config.SERVANT_DIR_STRUCTURE['voice']
                self._ensure_directory(voice_dir)

                for voice_url in details['voice_urls']:
                    filename = self._get_filename_from_url(voice_url)
                    save_path = self._get_file_save_path(voice_dir, filename)

                    if save_path.exists():
                        logger.debug(f"跳过已存在文件: {save_path}")
                        self.skipped_count += 1
                        continue

                    if self.download_file(voice_url, save_path):
                        self.downloaded_count += 1
                        logger.info(f"已下载: {save_path.name}")
                    else:
                        self.failed_count += 1
                        success = False

            return success

        except Exception as e:
            logger.error(f"下载英灵时出错: {servant_name} - {e}")
            return False

    def download_ce(self, ce_info: Dict[str, str],
                   scraper,
                   categories: List[str] = None) -> bool:
        """
        下载概念礼装的所有资源
        :param ce_info: 礼装信息字典
        :param scraper: WikiScraper实例
        :param categories: 要下载的资源类别
        :return: 下载是否成功
        """
        ce_name = ce_info['name']
        ce_url = ce_info['url']

        logger.info(f"开始下载概念礼装: {ce_name}")

        ce_dir = self.download_root / "概念礼装" / self._sanitize_filename(ce_name)
        self._ensure_directory(ce_dir)

        if categories is None:
            categories = list(config.CE_DIR_STRUCTURE.keys())

        try:
            details = scraper.get_ce_details(ce_url)
            if not details:
                logger.warning(f"无法获取礼装详情: {ce_name}")
                return False

            success = True

            if 'portrait' in categories and details.get('images'):
                portrait_dir = ce_dir / config.CE_DIR_STRUCTURE['portrait']
                self._ensure_directory(portrait_dir)

                for img_url in details['images']:
                    filename = self._get_filename_from_url(img_url)
                    save_path = self._get_file_save_path(portrait_dir, filename)

                    if save_path.exists():
                        logger.debug(f"跳过已存在文件: {save_path}")
                        self.skipped_count += 1
                        continue

                    if self.download_file(img_url, save_path):
                        self.downloaded_count += 1
                        logger.info(f"已下载: {save_path.name}")
                    else:
                        self.failed_count += 1
                        success = False

            if 'description' in categories and details.get('descriptions'):
                desc_dir = ce_dir / config.CE_DIR_STRUCTURE['description']
                self._ensure_directory(desc_dir)

                desc_file = desc_dir / '介绍.txt'
                with open(desc_file, 'w', encoding='utf-8') as f:
                    f.write(f"礼装名称: {ce_name}\n")
                    f.write(f"资料来源: {ce_url}\n\n")
                    for key, value in details['descriptions'].items():
                        f.write(f"【{key}】\n{value}\n\n")

                logger.info(f"已保存文字介绍: {desc_file.name}")

            return success

        except Exception as e:
            logger.error(f"下载礼装时出错: {ce_name} - {e}")
            return False

    def download_batch(self, items: List[Dict[str, str]],
                      download_func: Callable,
                      scraper,
                      categories: List[str] = None) -> Dict[str, int]:
        """
        批量下载资源
        :param items: 要下载的项目列表
        :param download_func: 下载函数
        :param scraper: WikiScraper实例
        :param categories: 要下载的资源类别
        :return: 统计信息
        """
        total = len(items)
        logger.info(f"开始批量下载，共 {total} 个项目")

        with ThreadPoolExecutor(max_workers=self.max_workers) as executor:
            futures = []

            for item in items:
                future = executor.submit(download_func, item, scraper, categories)
                futures.append(future)

            with tqdm(total=total, desc="下载进度", unit="项") as pbar:
                for future in as_completed(futures):
                    try:
                        future.result()
                    except Exception as e:
                        logger.error(f"下载任务出错: {e}")
                    pbar.update(1)

        return {
            'total': total,
            'downloaded': self.downloaded_count,
            'failed': self.failed_count,
            'skipped': self.skipped_count
        }

    def get_statistics(self) -> Dict[str, int]:
        """
        获取下载统计信息
        :return: 统计信息字典
        """
        return {
            'downloaded': self.downloaded_count,
            'failed': self.failed_count,
            'skipped': self.skipped_count,
            'total': self.downloaded_count + self.failed_count + self.skipped_count
        }

    def reset_statistics(self) -> None:
        """重置统计信息"""
        self.downloaded_count = 0
        self.failed_count = 0
        self.skipped_count = 0

    def check_disk_space(self) -> bool:
        """
        检查磁盘空间
        :return: 是否有足够空间
        """
        try:
            import shutil
            usage = shutil.disk_usage(self.download_root)
            free_gb = usage.free / (1024**3)
            logger.info(f"磁盘可用空间: {free_gb:.2f} GB")

            if free_gb < 1:
                logger.warning("磁盘空间不足，可能无法完成下载")
                return False
            return True
        except Exception as e:
            logger.warning(f"无法检查磁盘空间: {e}")
            return True

    def verify_downloads(self, base_dir: Path) -> Dict[str, int]:
        """
        验证下载的文件
        :param base_dir: 要验证的目录
        :return: 验证统计
        """
        stats = {
            'total_files': 0,
            'total_size': 0,
            'corrupted': 0,
            'missing': 0
        }

        if not base_dir.exists():
            return stats

        for root, dirs, files in os.walk(base_dir):
            for file in files:
                file_path = Path(root) / file
                stats['total_files'] += 1

                try:
                    size = file_path.stat().st_size
                    stats['total_size'] += size

                    if size == 0:
                        stats['corrupted'] += 1
                        logger.warning(f"文件为空: {file_path}")

                    if file.lower().endswith(('.png', '.jpg', '.jpeg', '.gif', '.webp')):
                        if not self._verify_image(file_path):
                            stats['corrupted'] += 1
                            logger.warning(f"图片文件可能损坏: {file_path}")

                except Exception as e:
                    stats['missing'] += 1
                    logger.error(f"验证文件失败: {file_path} - {e}")

        return stats

    def _verify_image(self, file_path: Path) -> bool:
        """
        验证图片文件
        :param file_path: 图片文件路径
        :return: 是否有效
        """
        try:
            with open(file_path, 'rb') as f:
                header = f.read(8)

                png_sig = b'\x89PNG\r\n\x1a\n'
                jpg_sig = b'\xff\xd8\xff'
                gif_sig = b'GIF89a'

                return (header.startswith(png_sig) or
                       header.startswith(jpg_sig) or
                       header.startswith(gif_sig))
        except:
            return False
