#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""对指定设计文案做全 UI 树探针，定位其实现位置或确认缺失。"""
import io
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
BASE = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")
OUT = os.path.join(ROOT, ".compile-temp", "probe.txt")

PROBES = {
    "acupoint": ["百会穴", "太阳穴", "风池穴", "膻中穴", "神阙穴", "合谷穴", "关元穴", "涌泉穴",
                 "督脉", "任脉", "共标记", "已选中", "穴位等级", "下一级", "眩晕",
                 "要穴", "施展点穴"],
    "menu-char-attributes": ["属性说明", "内功修为", "基础属性决定", "气血影响生存", "筋骨主攻伐",
                             "混沌世界 v1.2.0", "32ms"],
    "element-vision": ["视野已激活", "退出视野", "12.4m", "8.7m", "15.2m", "5.3m"],
    "popup-message": ["昆仑山中异象", "玄真子", "上清宫", "庚子年", "昆仑异象", "顿首"],
    "menu-equipment": ["套装", "龙纹之力", "鱼肠剑", "赤焰刀", "主武器", "饰品一", "饰品二", "8/24", "9/11"],
    "multiplayer": ["预计等待", "匹配范围", "当前匹配", "匹配队列", "房主", "加载更多", "已加载全部",
                    "个房间", "选择模式进入对应大厅", "排序", "人数", "房间名"],
    "menu-livelihood": ["技能总数", "2400/3200", "2,400 / 3,200", "85/100"],
    "reward-level-up": ["属性变化", "经验值", "0 / 5,000"],
    "popup-guide-side": ["疾跑", "二段跳", "墙面奔跑", "第 3 步", "共 8 步"],
    "ink-anim-ref-1": ["墨点初现", "晕染扩散", "淡墨层叠", "技术参数", "动画时间轴"],
}


def main():
    files = []
    for dp, _, fs in os.walk(BASE):
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.join(dp, f))
    cache = {}
    for fp in files:
        cache[fp] = io.open(fp, encoding="utf-8", errors="replace").read()
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as out:
        for page, ps in PROBES.items():
            out.write("### %s\n" % page)
            for p in ps:
                hits = []
                for fp, src in cache.items():
                    if p in src:
                        for i, l in enumerate(src.split("\n"), 1):
                            if p in l:
                                rel = os.path.relpath(fp, BASE).replace(os.sep, "/")
                                hits.append("%s:%d" % (rel, i))
                                break
                out.write("  %-18s %s\n" % (p, " ; ".join(hits[:4]) if hits else "--- MISSING ---"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
