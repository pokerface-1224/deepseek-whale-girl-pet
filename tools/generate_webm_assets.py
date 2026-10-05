#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成透明 WebM 视频资源 (VP9 + Alpha 通道)
用于鲸鱼娘桌宠动态形象升级
"""

import os
import sys
import math
import shutil
import tempfile
import subprocess
from PIL import Image, ImageOps

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART_DIR = os.path.join(BASE_DIR, "desktop-pet", "art")
OUT_DIR = os.path.join(ART_DIR, "webm")

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

# 目标视频分辨率 (偶数尺寸，满足 VP9 编码器要求，适合 HiDPI)
TARGET_WIDTH = 588
TARGET_HEIGHT = 672
FPS = 30

def create_blink_frame(img, eye_rect, blink_factor):
    """
    根据 blink_factor (0=完全睁开, 1=完全闭眼) 生成眨眼帧
    通过眼部区域压缩与上眼睑下拉模拟自然的眨眼
    """
    if blink_factor <= 0.01:
        return img
    
    frame = img.copy()
    x1, y1, x2, y2 = eye_rect
    eye_h = y2 - y1
    eye_w = x2 - x1
    
    if eye_h <= 4 or eye_w <= 4:
        return frame
        
    eye_crop = img.crop((x1, y1, x2, y2))
    # 压扁眼部
    new_h = max(2, int(eye_h * (1.0 - blink_factor * 0.85)))
    squashed = eye_crop.resize((eye_w, new_h), Image.Resampling.BILINEAR)
    
    # 填充眼皮颜色 (采样眼皮上方肤色)
    top_skin_box = (x1, max(0, y1 - 6), x2, y1)
    if top_skin_box[3] > top_skin_box[1]:
        skin_color = img.crop(top_skin_box).resize((eye_w, eye_h), Image.Resampling.BILINEAR)
        frame.paste(skin_color, (x1, y1), skin_color)
        
    paste_y = y1 + int((eye_h - new_h) * 0.7)
    frame.paste(squashed, (x1, paste_y), squashed)
    return frame

def generate_animation_frames(name, source_path, total_frames=45):
    """
    根据角色状态生成高质量透明帧序列
    """
    raw = Image.open(source_path).convert("RGBA")
    # 等比例缩放并居中放置在 TARGET 尺寸
    ratio = min(TARGET_WIDTH / raw.width, (TARGET_HEIGHT - 32) / raw.height)
    w = int(raw.width * ratio)
    h = int(raw.height * ratio)
    scaled = raw.resize((w, h), Image.Resampling.LANCZOS)
    
    frames = []
    
    # 眼睛大致区域 (相对于原图比例)
    eye_rect = (
        int(w * 0.22),
        int(h * 0.46),
        int(w * 0.78),
        int(h * 0.55)
    )
    
    for i in range(total_frames):
        t = i / float(total_frames)
        phase = t * 2.0 * math.pi
        
        # 针对不同状态设计专属动作曲线
        dy = 0.0
        angle = 0.0
        blink = 0.0
        scale_x = 1.0
        scale_y = 1.0
        
        if name == "front":
            # 常规待机：轻微呼吸起伏 + 周期性眨眼
            dy = math.sin(phase) * 6.0
            scale_y = 1.0 + math.sin(phase) * 0.015
            # 在 40% ~ 55% 处眨眼一次
            if 0.40 <= t <= 0.47:
                blink = math.sin((t - 0.40) / 0.07 * math.pi)
        elif name == "working":
            # 认真工作：快频打字微震 + 规律呼吸
            dy = math.sin(t * 4.0 * math.pi) * 3.0
            angle = math.sin(t * 2.0 * math.pi) * 0.8
        elif name == "playing":
            # 欢快玩耍：较大幅度的欢快蹦跳
            dy = -abs(math.sin(phase * 2.0)) * 16.0
            angle = math.sin(phase) * 3.0
        elif name == "slacking":
            # 摸鱼闲逛：温和的左右抱偶轻摇
            angle = math.sin(phase) * 2.2
            dy = math.sin(phase) * 4.0
        elif name == "thinking":
            # 沉思：轻微歪头
            angle = math.sin(phase) * 1.5
            dy = math.sin(phase) * 3.0
        elif name == "sleep":
            # 睡觉：平缓深长呼吸，闭眼
            dy = math.sin(phase) * 3.5
            scale_y = 1.0 + math.sin(phase) * 0.02
            blink = 1.0  # 常闭眼
        elif name == "eating":
            # 进食咀嚼：快速咀嚼动效
            dy = -abs(math.sin(t * 6.0 * math.pi)) * 8.0
            scale_y = 1.0 + math.sin(t * 6.0 * math.pi) * 0.02
        else:
            dy = math.sin(phase) * 4.0
            
        # 1. 眨眼处理 (仅限 front/side/sleep)
        if name in ("front", "side", "sleep") and blink > 0.01:
            char_img = create_blink_frame(scaled, eye_rect, blink)
        else:
            char_img = scaled
            
        # 2. 呼吸缩放与旋转
        if abs(scale_y - 1.0) > 0.001 or abs(scale_x - 1.0) > 0.001:
            nw = max(10, int(w * scale_x))
            nh = max(10, int(h * scale_y))
            transformed = char_img.resize((nw, nh), Image.Resampling.BILINEAR)
        else:
            transformed = char_img
            
        if abs(angle) > 0.05:
            transformed = transformed.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
            
        # 3. 复合到目标画布
        canvas = Image.new("RGBA", (TARGET_WIDTH, TARGET_HEIGHT), (0, 0, 0, 0))
        paste_x = (TARGET_WIDTH - transformed.width) // 2
        paste_y = TARGET_HEIGHT - transformed.height - 16 + int(dy)
        canvas.paste(transformed, (paste_x, paste_y), transformed)
        
        frames.append(canvas)
        
    return frames

def encode_webm(frames, output_path):
    """
    使用 ffmpeg 编码生成包含透明通道 (yuva420p) 的高质量 WebM 视频
    """
    temp_dir = tempfile.mkdtemp(prefix="whale_frames_")
    try:
        for idx, frame in enumerate(frames):
            frame_path = os.path.join(temp_dir, f"frame_{idx:04d}.png")
            frame.save(frame_path, "PNG")
            
        cmd = [
            "ffmpeg", "-y",
            "-framerate", str(FPS),
            "-i", os.path.join(temp_dir, "frame_%04d.png"),
            "-c:v", "libvpx-vp9",
            "-pix_fmt", "yuva420p",
            "-b:v", "1200k",
            "-crf", "28",
            "-auto-alt-ref", "0",
            "-metadata", "title=Whale Pet Transparent Animation",
            output_path
        ]
        res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if res.returncode != 0:
            print(f"Error encoding {output_path}:\n{res.stderr}", file=sys.stderr)
            return False
        return True
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)

def main():
    print(f"[WebM Generator] 扫描原画目录: {ART_DIR}")
    states = ["front", "working", "playing", "slacking", "thinking", "sleep", "eating", "side", "back"]
    generated = []
    
    for state in states:
        png_path = os.path.join(ART_DIR, f"{state}.png")
        if not os.path.exists(png_path):
            print(f"  [跳过] 未找到 {state}.png")
            continue
            
        out_webm = os.path.join(OUT_DIR, f"{state}.webm")
        print(f"  [处理中] 正在生成 {state}.webm ...")
        # 30-45 帧生成约 1-1.5 秒无缝循环
        frames = generate_animation_frames(state, png_path, total_frames=45)
        ok = encode_webm(frames, out_webm)
        if ok:
            size_kb = os.path.getsize(out_webm) / 1024.0
            print(f"    ✔ 成功生成: {state}.webm ({size_kb:.1f} KB)")
            generated.append(out_webm)
        else:
            print(f"    ❌ 生成失败: {state}.webm")
            
    print(f"\n[WebM Generator] 完成！共生成 {len(generated)} 个透明 WebM 视频至 {OUT_DIR}")

if __name__ == "__main__":
    main()
