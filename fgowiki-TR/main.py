#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
FGO Wiki资源下载器 - 主程序入口
用于下载FGO Wiki网站上的英灵和概念礼装资源

使用方法:
    python main.py              # 交互式界面
    python main.py --servants    # 仅下载英灵
    python main.py --ces        # 仅下载礼装
    python main.py --all         # 下载所有
    python main.py --verify     # 验证下载文件
"""

import sys
import argparse
import logging
from pathlib import Path

import config
from scraper import WikiScraper
from downloader import Downloader


def setup_logging():
    """配置日志系统"""
    logging.basicConfig(
        level=getattr(logging, config.LOG_LEVEL),
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        handlers=[
            logging.FileHandler(config.LOG_FILE, encoding='utf-8'),
            logging.StreamHandler()
        ]
    )
    return logging.getLogger(__name__)


def download_servants(downloader, scraper, max_pages=None):
    """下载英灵资源"""
    logger = logging.getLogger(__name__)
    logger.info("开始下载英灵资源")

    servants = scraper.get_servant_list(max_pages=max_pages)
    if not servants:
        logger.error("未能获取英灵列表")
        return False

    logger.info(f"获取到 {len(servants)} 个英灵")
    downloader.reset_statistics()
    stats = downloader.download_batch(
        servants,
        downloader.download_servant,
        scraper,
        categories=['portrait', 'description']
    )

    logger.info(f"英灵下载完成: {stats}")
    return True


def download_ces(downloader, scraper, max_pages=None):
    """下载概念礼装资源"""
    logger = logging.getLogger(__name__)
    logger.info("开始下载概念礼装资源")

    ces = scraper.get_ce_list(max_pages=max_pages)
    if not ces:
        logger.error("未能获取礼装列表")
        return False

    logger.info(f"获取到 {len(ces)} 个概念礼装")
    downloader.reset_statistics()
    stats = downloader.download_batch(
        ces,
        downloader.download_ce,
        scraper,
        categories=['portrait', 'description']
    )

    logger.info(f"礼装下载完成: {stats}")
    return True


def verify_downloads(downloader):
    """验证下载文件"""
    logger = logging.getLogger(__name__)
    download_dir = Path(config.DOWNLOAD_ROOT)

    if not download_dir.exists():
        logger.warning("下载目录不存在")
        return

    logger.info("开始验证下载文件...")

    servant_dir = download_dir / "英灵"
    if servant_dir.exists():
        servant_stats = downloader.verify_downloads(servant_dir)
        logger.info(f"英灵文件验证: {servant_stats}")

    ce_dir = download_dir / "概念礼装"
    if ce_dir.exists():
        ce_stats = downloader.verify_downloads(ce_dir)
        logger.info(f"礼装文件验证: {ce_stats}")

    logger.info("验证完成")


def main():
    """主函数"""
    parser = argparse.ArgumentParser(
        description='FGO Wiki资源下载器',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
示例:
  %(prog)s --servants        下载所有英灵
  %(prog)s --ces --pages 5   下载前5页礼装
  %(prog)s --all             下载所有资源
  %(prog)s --verify          验证下载的文件
        """
    )

    parser.add_argument('--servants', action='store_true',
                       help='下载英灵资源')
    parser.add_argument('--ces', action='store_true',
                       help='下载概念礼装资源')
    parser.add_argument('--all', action='store_true',
                       help='下载所有资源')
    parser.add_argument('--pages', type=int, default=None,
                       help='限制下载页数')
    parser.add_argument('--verify', action='store_true',
                       help='验证已下载的文件')
    parser.add_argument('--workers', type=int, default=config.MAX_WORKERS,
                       help=f'并发下载线程数 (默认: {config.MAX_WORKERS})')
    parser.add_argument('--quiet', '-q', action='store_true',
                       help='安静模式，减少输出')

    args = parser.parse_args()

    if args.quiet:
        logging.getLogger().setLevel(logging.WARNING)

    logger = setup_logging()
    logger.info("FGO Wiki资源下载器启动")

    scraper = WikiScraper()
    downloader = Downloader(max_workers=args.workers)

    if args.verify:
        verify_downloads(downloader)
        return 0

    if args.all:
        logger.info("下载模式: 所有资源")
        download_servants(downloader, scraper, args.pages)
        download_ces(downloader, scraper, args.pages)
    elif args.servants:
        logger.info("下载模式: 英灵")
        download_servants(downloader, scraper, args.pages)
    elif args.ces:
        logger.info("下载模式: 概念礼装")
        download_ces(downloader, scraper, args.pages)
    else:
        from cli import FGOWikiCLI
        cli = FGOWikiCLI()
        cli.run()

    logger.info("程序执行完成")
    return 0


if __name__ == '__main__':
    sys.exit(main())
