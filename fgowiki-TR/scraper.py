# -*- coding: utf-8 -*-
"""
FGO Wiki 网页抓取模块
负责从FGO Wiki网站抓取英灵和礼装信息
"""

import re
import time
import logging
from typing import List, Dict, Optional, Tuple
from urllib.parse import urljoin, quote
import requests
from bs4 import BeautifulSoup

import config

logger = logging.getLogger(__name__)


class WikiScraper:
    """FGO Wiki网页抓取器"""

    def __init__(self):
        self.session = requests.Session()
        self.session.headers.update(config.HEADERS)
        self.base_url = config.WIKI_BASE_URL

    def _make_request(self, url: str, retries: int = config.MAX_RETRIES) -> Optional[str]:
        """
        发送HTTP请求获取页面内容
        :param url: 请求的URL
        :param retries: 重试次数
        :return: 页面HTML内容，失败返回None
        """
        for attempt in range(retries):
            try:
                logger.info(f"请求页面: {url} (尝试 {attempt + 1}/{retries})")
                response = self.session.get(url, timeout=config.REQUEST_TIMEOUT)
                response.raise_for_status()
                response.encoding = 'utf-8'
                time.sleep(config.REQUEST_DELAY)
                return response.text
            except requests.RequestException as e:
                logger.warning(f"请求失败: {e}")
                if attempt < retries - 1:
                    time.sleep(config.REQUEST_DELAY * (attempt + 1))
                else:
                    logger.error(f"请求 {url} 最终失败")
                    return None

    def get_servant_list(self, max_pages: int = None) -> List[Dict[str, str]]:
        """
        获取英灵列表
        :param max_pages: 最大页数限制，None表示获取全部
        :return: 英灵信息列表，每个字典包含 name, url, no
        """
        servants = []
        page = 1
        current_url = config.SERVANT_LIST_URL

        while current_url:
            logger.info(f"正在获取英灵列表第 {page} 页...")

            html = self._make_request(current_url)
            if not html:
                break

            soup = BeautifulSoup(html, 'html.parser')

            table = soup.find('table', class_='servant-list')
            if not table:
                table = soup.find('table', {'id': 'servant-table'})

            if table:
                rows = table.find_all('tr')[1:]
                for row in rows:
                    cells = row.find_all('td')
                    if len(cells) >= 3:
                        no_cell = cells[0]
                        name_cell = cells[2]
                        link = name_cell.find('a')

                        if link:
                            servant_no = no_cell.get_text(strip=True)
                            servant_name = link.get_text(strip=True)
                            servant_url = urljoin(self.base_url, link.get('href', ''))

                            servants.append({
                                'no': servant_no,
                                'name': servant_name,
                                'url': servant_url
                            })

            pagination = soup.find('a', text=re.compile(r'下一页|›'))
            if pagination and (max_pages is None or page < max_pages):
                next_url = pagination.get('href')
                if next_url:
                    current_url = urljoin(self.base_url, next_url)
                    page += 1
                else:
                    break
            else:
                break

        logger.info(f"共获取 {len(servants)} 个英灵")
        return servants

    def get_ce_list(self, max_pages: int = None) -> List[Dict[str, str]]:
        """
        获取概念礼装列表
        :param max_pages: 最大页数限制
        :return: 礼装信息列表
        """
        ces = []
        page = 1
        current_url = config.CE_LIST_URL

        while current_url:
            logger.info(f"正在获取礼装列表第 {page} 页...")

            html = self._make_request(current_url)
            if not html:
                break

            soup = BeautifulSoup(html, 'html.parser')

            tables = soup.find_all('table', class_='ce-list')
            if not tables:
                tables = soup.find_all('table', {'id': 'ce-table'})
            if not tables:
                tables = soup.find_all('table')

            for table in tables:
                rows = table.find_all('tr')[1:]
                for row in rows:
                    cells = row.find_all('td')
                    if len(cells) >= 3:
                        no_cell = cells[0]
                        name_cell = cells[2]
                        link = name_cell.find('a')

                        if link:
                            ce_no = no_cell.get_text(strip=True)
                            ce_name = link.get_text(strip=True)
                            ce_url = urljoin(self.base_url, link.get('href', ''))

                            ces.append({
                                'no': ce_no,
                                'name': ce_name,
                                'url': ce_url
                            })

            pagination = soup.find('a', text=re.compile(r'下一页|›'))
            if pagination and (max_pages is None or page < max_pages):
                next_url = pagination.get('href')
                if next_url:
                    current_url = urljoin(self.base_url, next_url)
                    page += 1
                else:
                    break
            else:
                break

        logger.info(f"共获取 {len(ces)} 个概念礼装")
        return ces

    def get_servant_details(self, url: str) -> Dict[str, any]:
        """
        获取英灵详情页面信息
        :param url: 英灵页面URL
        :return: 包含各类资源链接的字典
        """
        html = self._make_request(url)
        if not html:
            return {}

        soup = BeautifulSoup(html, 'html.parser')
        details = {
            'images': [],
            'voice_urls': [],
            'descriptions': {}
        }

        gallery = soup.find('div', class_='gallery')
        if gallery:
            items = gallery.find_all('a', class_='image')
            for item in items:
                img = item.find('img')
                if img:
                    img_url = img.get('src') or img.get('data-src')
                    if img_url:
                        details['images'].append(img_url)

        info_box = soup.find('table', class_='infobox')
        if info_box:
            images = info_box.find_all('img')
            for img in images:
                img_url = img.get('src') or img.get('data-src')
                if img_url and 'media.fgo.wiki' in img_url:
                    details['images'].append(img_url)

        all_images = soup.find_all('img')
        for img in all_images:
            img_url = img.get('src') or img.get('data-src')
            if img_url and 'media.fgo.wiki' in img_url:
                if img_url not in details['images']:
                    details['images'].append(img_url)

        content_div = soup.find('div', class_='mw-parser-output')
        if content_div:
            details['descriptions']['profile'] = content_div.get_text(strip=True)[:5000]
            audio_tags = content_div.find_all('audio')
            for audio in audio_tags:
                src = audio.get('src')
                if src:
                    details['voice_urls'].append(src)

        return details

    def get_ce_details(self, url: str) -> Dict[str, any]:
        """
        获取概念礼装详情页面信息
        :param url: 礼装页面URL
        :return: 包含各类资源链接的字典
        """
        html = self._make_request(url)
        if not html:
            return {}

        soup = BeautifulSoup(html, 'html.parser')
        details = {
            'images': [],
            'descriptions': {}
        }

        gallery = soup.find('div', class_='gallery')
        if gallery:
            items = gallery.find_all('a', class_='image')
            for item in items:
                img = item.find('img')
                if img:
                    img_url = img.get('src') or img.get('data-src')
                    if img_url:
                        details['images'].append(img_url)

        info_box = soup.find('table', class_='infobox')
        if info_box:
            images = info_box.find_all('img')
            for img in images:
                img_url = img.get('src') or img.get('data-src')
                if img_url and 'media.fgo.wiki' in img_url:
                    details['images'].append(img_url)

        all_images = soup.find_all('img')
        for img in all_images:
            img_url = img.get('src') or img.get('data-src')
            if img_url and 'media.fgo.wiki' in img_url:
                if img_url not in details['images']:
                    details['images'].append(img_url)

        content_div = soup.find('div', class_='mw-parser-output')
        if content_div:
            details['descriptions']['profile'] = content_div.get_text(strip=True)[:5000]

        return details

    def extract_image_urls_from_page(self, html: str) -> List[str]:
        """
        从页面HTML中提取所有图片URL
        :param html: 页面HTML内容
        :return: 图片URL列表
        """
        soup = BeautifulSoup(html, 'html.parser')
        image_urls = []

        all_images = soup.find_all('img')
        for img in all_images:
            img_url = img.get('src') or img.get('data-src')
            if img_url and 'media.fgo.wiki' in img_url:
                if img_url.startswith('//'):
                    img_url = 'https:' + img_url
                image_urls.append(img_url)

        gallery_links = soup.find_all('a', class_='image')
        for link in gallery_links:
            href = link.get('href', '')
            if 'media.fgo.wiki' in href:
                if href.startswith('//'):
                    href = 'https:' + href
                image_urls.append(href)

        return list(set(image_urls))

    def get_all_page_urls(self, list_url: str) -> List[str]:
        """
        获取列表页所有详情页URL
        :param list_url: 列表页URL
        :return: 详情页URL列表
        """
        urls = []
        html = self._make_request(list_url)
        if not html:
            return urls

        soup = BeautifulSoup(html, 'html.parser')
        links = soup.find_all('a', href=True)

        for link in links:
            href = link.get('href', '')
            if '/w/' in href and href not in [config.SERVANT_LIST_URL, config.CE_LIST_URL]:
                if not any(x in href for x in ['英灵图鉴', '礼装图鉴', '特殊:', '文件:', 'Template:', '帮助:']):
                    full_url = urljoin(self.base_url, href)
                    if full_url not in urls:
                        urls.append(full_url)

        return urls
