# -*- coding: utf-8 -*-
"""
FGO Wiki下载器 - 命令行界面
提供交互式菜单操作
"""

import os
import sys
import logging
from pathlib import Path
from typing import List, Dict, Optional

import config
from scraper import WikiScraper
from downloader import Downloader

logger = logging.getLogger(__name__)


class FGOWikiCLI:
    """FGO Wiki下载器命令行界面"""

    def __init__(self):
        self.scraper = WikiScraper()
        self.downloader = Downloader()
        self.setup_logging()

    def setup_logging(self):
        """配置日志系统"""
        logging.basicConfig(
            level=getattr(logging, config.LOG_LEVEL),
            format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
            handlers=[
                logging.FileHandler(config.LOG_FILE, encoding='utf-8'),
                logging.StreamHandler()
            ]
        )

    def print_banner(self):
        """打印程序标题"""
        banner = """
╔═══════════════════════════════════════════════════════╗
║         FGO Wiki 资源下载器 v1.0                      ║
║         批量下载英灵和概念礼装资源                     ║
╚═══════════════════════════════════════════════════════╝
        """
        print(banner)

    def print_menu(self):
        """打印主菜单"""
        menu = """
请选择操作:

【下载选项】
1. 下载所有英灵资源
2. 下载所有概念礼装资源
3. 下载所有资源（英灵+礼装）
4. 自定义下载范围

【管理选项】
5. 查看下载统计
6. 验证已下载文件
7. 查看磁盘空间

【工具选项】
8. 导出资源列表
9. 清理重复文件

【其他】
0. 退出程序

请输入选项编号 (0-9):
"""
        print(menu)

    def download_servants(self, max_pages: int = None):
        """下载所有英灵资源"""
        print("\n正在获取英灵列表...")
        servants = self.scraper.get_servant_list(max_pages=max_pages)

        if not servants:
            print("未获取到任何英灵信息，请检查网络连接")
            return

        print(f"\n获取到 {len(servants)} 个英灵")
        print("开始下载...\n")

        self.downloader.reset_statistics()
        stats = self.downloader.download_batch(
            servants,
            self.downloader.download_servant,
            self.scraper,
            categories=['portrait', 'description']
        )

        self.print_download_stats(stats)

    def download_ces(self, max_pages: int = None):
        """下载所有概念礼装资源"""
        print("\n正在获取概念礼装列表...")
        ces = self.scraper.get_ce_list(max_pages=max_pages)

        if not ces:
            print("未获取到任何礼装信息，请检查网络连接")
            return

        print(f"\n获取到 {len(ces)} 个概念礼装")
        print("开始下载...\n")

        self.downloader.reset_statistics()
        stats = self.downloader.download_batch(
            ces,
            self.downloader.download_ce,
            self.scraper,
            categories=['portrait', 'description']
        )

        self.print_download_stats(stats)

    def download_all(self, max_pages: int = None):
        """下载所有资源"""
        print("\n开始下载所有资源...")
        print("第一步：下载英灵资源\n")
        self.download_servants(max_pages)

        print("\n第二步：下载概念礼装资源\n")
        self.download_ces(max_pages)

        print("\n全部下载完成！")

    def custom_download(self):
        """自定义下载选项"""
        print("\n自定义下载设置")
        print("-" * 50)

        download_type = input("下载类型 (1-英灵, 2-礼装, 3-两者): ").strip()
        max_pages_input = input("限制下载页数 (直接回车下载全部): ").strip()
        max_pages = int(max_pages_input) if max_pages_input.isdigit() else None

        if download_type == "1":
            self.download_servants(max_pages)
        elif download_type == "2":
            self.download_ces(max_pages)
        elif download_type == "3":
            self.download_all(max_pages)
        else:
            print("无效的选择")

    def view_statistics(self):
        """查看下载统计"""
        print("\n下载统计")
        print("=" * 50)

        stats = self.downloader.get_statistics()
        print(f"已下载文件数: {stats['downloaded']}")
        print(f"失败文件数: {stats['failed']}")
        print(f"跳过文件数: {stats['skipped']}")
        print(f"总处理文件数: {stats['total']}")

        download_dir = Path(config.DOWNLOAD_ROOT)
        if download_dir.exists():
            servant_dir = download_dir / "英灵"
            ce_dir = download_dir / "概念礼装"

            servant_count = len(list(servant_dir.rglob('*'))) if servant_dir.exists() else 0
            ce_count = len(list(ce_dir.rglob('*'))) if ce_dir.exists() else 0

            print(f"\n目录统计:")
            print(f"英灵目录文件数: {servant_count}")
            print(f"礼装目录文件数: {ce_count}")

    def verify_downloads(self):
        """验证下载的文件"""
        print("\n正在验证下载的文件...")
        download_dir = Path(config.DOWNLOAD_ROOT)

        if not download_dir.exists():
            print("下载目录不存在")
            return

        print("验证英灵资源...")
        servant_dir = download_dir / "英灵"
        if servant_dir.exists():
            servant_stats = self.downloader.verify_downloads(servant_dir)
            print(f"英灵文件验证完成:")
            print(f"  总文件数: {servant_stats['total_files']}")
            print(f"  损坏文件: {servant_stats['corrupted']}")
            print(f"  缺失文件: {servant_stats['missing']}")
            print(f"  总大小: {servant_stats['total_size'] / 1024 / 1024:.2f} MB")

        print("\n验证礼装资源...")
        ce_dir = download_dir / "概念礼装"
        if ce_dir.exists():
            ce_stats = self.downloader.verify_downloads(ce_dir)
            print(f"礼装文件验证完成:")
            print(f"  总文件数: {ce_stats['total_files']}")
            print(f"  损坏文件: {ce_stats['corrupted']}")
            print(f"  缺失文件: {ce_stats['missing']}")
            print(f"  总大小: {ce_stats['total_size'] / 1024 / 1024:.2f} MB")

    def check_disk_space(self):
        """查看磁盘空间"""
        print("\n磁盘空间检查")
        print("=" * 50)

        if self.downloader.check_disk_space():
            download_dir = Path(config.DOWNLOAD_ROOT)
            if download_dir.exists():
                total_size = sum(f.stat().st_size for f in download_dir.rglob('*') if f.is_file())
                print(f"下载目录总大小: {total_size / 1024 / 1024 / 1024:.2f} GB")

    def export_list(self):
        """导出资源列表"""
        print("\n导出资源列表")
        print("=" * 50)

        download_dir = Path(config.DOWNLOAD_ROOT)
        if not download_dir.exists():
            print("下载目录不存在")
            return

        list_file = download_dir / "资源列表.txt"

        with open(list_file, 'w', encoding='utf-8') as f:
            f.write("FGO Wiki 资源下载列表\n")
            f.write("=" * 50 + "\n\n")

            servant_dir = download_dir / "英灵"
            if servant_dir.exists():
                f.write("【英灵】\n")
                for servant_folder in sorted(servant_dir.iterdir()):
                    if servant_folder.is_dir():
                        files = list(servant_folder.rglob('*'))
                        file_count = len([f for f in files if f.is_file()])
                        f.write(f"  {servant_folder.name} ({file_count} 个文件)\n")
                f.write("\n")

            ce_dir = download_dir / "概念礼装"
            if ce_dir.exists():
                f.write("【概念礼装】\n")
                for ce_folder in sorted(ce_dir.iterdir()):
                    if ce_folder.is_dir():
                        files = list(ce_folder.rglob('*'))
                        file_count = len([f for f in files if f.is_file()])
                        f.write(f"  {ce_folder.name} ({file_count} 个文件)\n")

        print(f"资源列表已导出到: {list_file}")

    def cleanup_duplicates(self):
        """清理重复文件"""
        print("\n清理重复文件")
        print("=" * 50)

        download_dir = Path(config.DOWNLOAD_ROOT)
        if not download_dir.exists():
            print("下载目录不存在")
            return

        removed_count = 0

        for folder in download_dir.rglob('*'):
            if folder.is_dir():
                files = list(folder.glob('*'))
                seen = {}

                for file in files:
                    if file.is_file():
                        name = file.stem
                        ext = file.suffix.lower()

                        if name not in seen:
                            seen[name] = file
                        else:
                            if file.stat().st_size <= seen[name].stat().st_size:
                                file.unlink()
                                removed_count += 1
                                print(f"删除重复文件: {file}")
                            else:
                                seen[name].unlink()
                                removed_count += 1
                                print(f"删除重复文件: {seen[name]}")
                                seen[name] = file

        print(f"\n清理完成，共删除 {removed_count} 个重复文件")

    def print_download_stats(self, stats: Dict[str, int]):
        """打印下载统计"""
        print("\n" + "=" * 50)
        print("下载统计")
        print("=" * 50)
        print(f"总项目数: {stats['total']}")
        print(f"成功下载: {stats['downloaded']}")
        print(f"下载失败: {stats['failed']}")
        print(f"跳过文件: {stats['skipped']}")

        if stats['total'] > 0:
            success_rate = (stats['downloaded'] / stats['total']) * 100
            print(f"成功率: {success_rate:.1f}%")
        print("=" * 50)

    def run(self):
        """运行主程序"""
        self.print_banner()

        while True:
            self.print_menu()
            choice = input().strip()

            try:
                if choice == '1':
                    self.download_servants()
                elif choice == '2':
                    self.download_ces()
                elif choice == '3':
                    self.download_all()
                elif choice == '4':
                    self.custom_download()
                elif choice == '5':
                    self.view_statistics()
                elif choice == '6':
                    self.verify_downloads()
                elif choice == '7':
                    self.check_disk_space()
                elif choice == '8':
                    self.export_list()
                elif choice == '9':
                    self.cleanup_duplicates()
                elif choice == '0':
                    print("\n感谢使用，再见！")
                    break
                else:
                    print("\n无效的选择，请重新输入")

                input("\n按回车键继续...")

            except KeyboardInterrupt:
                print("\n\n程序被用户中断")
                break
            except Exception as e:
                print(f"\n发生错误: {e}")
                logger.error(f"CLI错误: {e}", exc_info=True)
                input("\n按回车键继续...")


if __name__ == '__main__':
    cli = FGOWikiCLI()
    cli.run()
