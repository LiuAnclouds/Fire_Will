from __future__ import annotations

import argparse
import ctypes
import json
import math
import time
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np
from PIL import ImageGrab


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="War3 template recognition probe")
    parser.add_argument("--info", default="info", help="template folder")
    parser.add_argument("--out", default="outputs/recognition_probe", help="output folder")
    parser.add_argument("--delay", type=float, default=3.0, help="seconds before capture starts")
    parser.add_argument("--watch", type=float, default=8.0, help="seconds to keep scanning")
    parser.add_argument("--interval", type=float, default=0.25, help="seconds between scans")
    parser.add_argument("--threshold", type=float, default=0.88, help="hit threshold")
    parser.add_argument("--scales", default="0.92,0.96,1.0,1.04,1.08", help="comma separated template scales")
    return parser.parse_args()


def read_image(path: Path) -> np.ndarray:
    data = np.fromfile(str(path), dtype=np.uint8)
    img = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if img is None:
        raise RuntimeError(f"Cannot read image: {path}")
    return img


def write_image(path: Path, img: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    ok, encoded = cv2.imencode(".png", img)
    if not ok:
        raise RuntimeError(f"Cannot encode image: {path}")
    encoded.tofile(str(path))


def grab_screen() -> tuple[np.ndarray, tuple[int, int]]:
    try:
        pil_img = ImageGrab.grab(all_screens=True)
        origin = virtual_screen_origin()
    except TypeError:
        pil_img = ImageGrab.grab()
        origin = (0, 0)
    screen = cv2.cvtColor(np.array(pil_img), cv2.COLOR_RGB2BGR)
    return screen, origin


def virtual_screen_origin() -> tuple[int, int]:
    try:
        user32 = ctypes.windll.user32
        return int(user32.GetSystemMetrics(76)), int(user32.GetSystemMetrics(77))
    except Exception:
        return 0, 0


def parse_scales(value: str) -> list[float]:
    scales: list[float] = []
    for part in value.split(","):
        part = part.strip()
        if not part:
            continue
        try:
            scale = float(part)
        except ValueError:
            continue
        if 0.5 <= scale <= 2.0:
            scales.append(scale)
    return scales or [1.0]


def match_template(screen_bgr: np.ndarray, template_bgr: np.ndarray, scales: list[float]) -> dict:
    best = {
        "score": -1.0,
        "x": 0,
        "y": 0,
        "w": template_bgr.shape[1],
        "h": template_bgr.shape[0],
        "scale": 1.0,
    }

    screen_h, screen_w = screen_bgr.shape[:2]

    for scale in scales:
        if math.isclose(scale, 1.0):
            templ = template_bgr
        else:
            new_w = max(4, int(round(template_bgr.shape[1] * scale)))
            new_h = max(4, int(round(template_bgr.shape[0] * scale)))
            templ = cv2.resize(template_bgr, (new_w, new_h), interpolation=cv2.INTER_AREA)

        h, w = templ.shape[:2]
        if h >= screen_h or w >= screen_w:
            continue

        response = cv2.matchTemplate(screen_bgr, templ, cv2.TM_CCOEFF_NORMED)
        _, max_val, _, max_loc = cv2.minMaxLoc(response)
        if max_val > best["score"]:
            best = {
                "score": float(max_val),
                "x": int(max_loc[0]),
                "y": int(max_loc[1]),
                "w": int(w),
                "h": int(h),
                "scale": float(scale),
            }

    return best


def draw_result(screen: np.ndarray, result: dict, label: str, threshold: float) -> np.ndarray:
    img = screen.copy()
    color = (0, 220, 0) if result["score"] >= threshold else (0, 165, 255)
    x, y, w, h = result["x"], result["y"], result["w"], result["h"]
    cv2.rectangle(img, (x, y), (x + w, y + h), color, 2)
    cv2.drawMarker(img, (x + w // 2, y + h // 2), color, markerType=cv2.MARKER_CROSS, markerSize=18, thickness=2)
    cv2.putText(
        img,
        f"{label} score={result['score']:.3f} scale={result['scale']:.2f}",
        (max(0, x), max(18, y - 8)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        color,
        2,
        cv2.LINE_AA,
    )
    return img


def safe_stem(path: Path) -> str:
    return "".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in path.stem)


def main() -> int:
    args = parse_args()
    info_dir = Path(args.info)
    out_dir = Path(args.out)
    threshold = args.threshold
    scales = parse_scales(args.scales)

    templates = sorted(info_dir.glob("*.png"))
    if not templates:
        print(f"没有找到模板图片：{info_dir.resolve()}")
        return 1

    loaded = [(path, read_image(path)) for path in templates]
    print("识别测试器已启动。")
    print(f"模板目录：{info_dir.resolve()}")
    print(f"模板数量：{len(loaded)}")
    print(f"倒计时 {args.delay:.1f} 秒，请切回游戏画面。")
    time.sleep(max(0.0, args.delay))

    start = time.perf_counter()
    end = start + max(args.watch, 0.1)
    best_by_name: dict[str, dict] = {}
    best_frame_by_name: dict[str, np.ndarray] = {}
    frames = 0
    origin = (0, 0)

    while time.perf_counter() < end:
        screen, origin = grab_screen()
        frames += 1

        for path, template in loaded:
            result = match_template(screen, template, scales)
            result["template"] = path.name
            result["screen_x"] = result["x"] + origin[0]
            result["screen_y"] = result["y"] + origin[1]
            result["center_x"] = result["screen_x"] + result["w"] // 2
            result["center_y"] = result["screen_y"] + result["h"] // 2
            old = best_by_name.get(path.name)
            if old is None or result["score"] > old["score"]:
                best_by_name[path.name] = result
                best_frame_by_name[path.name] = screen.copy()

        time.sleep(max(0.03, args.interval))

    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir.mkdir(parents=True, exist_ok=True)
    report = {
        "time": stamp,
        "threshold": threshold,
        "scales": scales,
        "frames": frames,
        "origin": {"x": origin[0], "y": origin[1]},
        "results": [],
    }

    print("")
    print("识别结果：")
    for path, _ in loaded:
        result = best_by_name[path.name]
        hit = result["score"] >= threshold
        label = "HIT" if hit else "LOW"
        print(
            f"[{label}] {path.name} | score={result['score']:.3f} | "
            f"left/top=({result['screen_x']},{result['screen_y']}) | "
            f"center=({result['center_x']},{result['center_y']}) | scale={result['scale']:.2f}"
        )
        annotated = draw_result(best_frame_by_name[path.name], result, safe_stem(path), threshold)
        image_path = out_dir / f"{stamp}_{safe_stem(path)}.png"
        write_image(image_path, annotated)
        result["hit"] = hit
        result["annotated"] = str(image_path.resolve())
        report["results"].append(result)

    report_path = out_dir / f"{stamp}_report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    latest_path = out_dir / "latest_report.json"
    latest_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print("")
    print(f"报告：{report_path.resolve()}")
    print(f"最新报告：{latest_path.resolve()}")
    print(f"带框截图目录：{out_dir.resolve()}")
    print("")
    print("经验判断：score >= 0.92 通常很稳；0.88-0.92 需要看截图；低于 0.88 不建议自动点击。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
