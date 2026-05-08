# -*- coding: utf-8 -*-
"""
FGO Wiki资源下载器配置文件
"""

# 下载根目录
DOWNLOAD_ROOT = "D:/code-x/fgowiki/fgowiki-TR/downloads"

# FGO Wiki 网站地址
WIKI_BASE_URL = "https://m.fgo.wiki"
WIKI_FULL_URL = "https://fgo.wiki"

# 图片资源基础URL
MEDIA_BASE_URL = "https://media.fgo.wiki"

# 下载设置
MAX_WORKERS = 5  # 并发下载线程数
REQUEST_DELAY = 1.0  # 请求间隔(秒)，避免过于频繁
REQUEST_TIMEOUT = 30  # 请求超时时间(秒)
MAX_RETRIES = 3  # 最大重试次数

# 下载资源类型
RESOURCE_TYPES = {
    "portrait": "立绘",           # 满破立绘
    "icon": "头像",              # 头像图标
    "battle": "战斗形象",        # 战斗形象
    "sprite": "灵衣",           # 灵衣形象
    "description": "文字解说",   # 角色/礼装说明
}

# 角色目录结构
SERVANT_DIR_STRUCTURE = {
    "portrait": "立绘",
    "icon": "头像",
    "battle": "战斗形象",
    "sprite": "灵衣",
    "voice": "语音",
    "description": "文字解说",
}

# 礼装目录结构
CE_DIR_STRUCTURE = {
    "portrait": "立绘",
    "icon": "图标",
    "description": "文字解说",
}

# 日志配置
LOG_FILE = "fgowiki_downloader.log"
LOG_LEVEL = "INFO"

# URL列表页
SERVANT_LIST_URL = "https://m.fgo.wiki/w/英灵图鉴"
CE_LIST_URL = "https://m.fgo.wiki/w/礼装图鉴"

# 用户浏览器标识
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

# 请求头
HEADERS = {
    "User-Agent": USER_AGENT,
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
    "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
    "Connection": "keep-alive",
}
