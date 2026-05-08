# -*- coding: utf-8 -*-
"""
FGO Wiki 资源下载器
Fate/Grand Order Wiki Resource Downloader

一个用于从 FGO Wiki 下载英灵和概念礼装资源的自动化工具。
"""

__version__ = "1.0.0"
__author__ = "FGO Wiki Downloader Team"

from .scraper import WikiScraper
from .downloader import Downloader
from .cli import FGOWikiCLI

__all__ = ['WikiScraper', 'Downloader', 'FGOWikiCLI']
