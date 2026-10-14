#!/usr/bin/env python3
"""Export PSD layers as cropped PNGs and a Unity layout JSON."""

import argparse
import json
import math
import re
import sys
from pathlib import Path


def output_directory(project_root, assets_path):
    root = (Path(project_root).resolve() / "Assets").resolve()
    relative = Path(assets_path.replace("\\", "/"))
    if not root.is_dir() or relative.is_absolute() or len(relative.parts) < 2 or relative.parts[0].lower() != "assets":
        raise ValueError("UI 导出路径必须以 Assets/ 开头")
    target = (Path(project_root).resolve() / relative).resolve()
    if target == root or root not in target.parents:
        raise ValueError("UI 导出路径不能超出 Assets")
    return target


def source_file(project_root, source):
    relative = Path(source.replace("\\", "/"))
    if relative.is_absolute():
        raise ValueError("PSD 源路径必须相对项目根目录")
    root = Path(project_root).resolve()
    path = (root / relative).resolve()
    if root not in path.parents or path.suffix.lower() != ".psd" or not path.is_file():
        raise ValueError("找不到项目内的 PSD 文件，或路径不是 .psd 文件")
    return path


def safe_name(name):
    return re.sub(r"[^\w-]+", "_", name, flags=re.UNICODE).strip("_") or "layer"


def source_files(project_root, source):
    relative = Path(source.replace("\\", "/"))
    if relative.is_absolute():
        raise ValueError("PSD 源路径必须相对项目根目录")
    root = Path(project_root).resolve()
    path = (root / relative).resolve()
    if root not in path.parents:
        raise ValueError("PSD 源路径不能超出项目目录")
    if path.is_file() and path.suffix.lower() == ".psd":
        return [path]
    if path.is_dir():
        files = sorted((item for item in path.rglob("*")
                        if item.is_file() and item.suffix.lower() == ".psd"),
                       key=lambda item: item.name.lower())
        if files:
            return files
    raise ValueError("找不到项目内的 PSD 文件或文件夹中没有 PSD 文件")


def has_green_label(layer):
    return getattr(layer.tagged_blocks.get_data(b"lclr"), "name", "") == "GREEN"


def export_pages(psd, path):
    boards = [layer for layer in psd.descendants() if layer.kind == "artboard"]
    if boards:
        return [(safe_name(board.name), board, board.bbox, not board.visible) for board in boards]
    return [(path.stem, psd, (0, 0, psd.width, psd.height), False)]


def value(item):
    return getattr(item, "value", item)


def text_bounds(layer):
    """Use the Photoshop text box, transformed into canvas coordinates."""
    bounds = layer._data.text_data.get(b"bounds")
    if bounds is None:
        return layer.bbox
    left = float(bounds[b"Left"])
    top = float(bounds[b"Top "])
    right = float(bounds[b"Rght"])
    bottom = float(bounds[b"Btom"])
    xx, xy, yx, yy, tx, ty = layer.transform
    corners = [(xx * x + yx * y + tx, xy * x + yy * y + ty)
               for x in (left, right) for y in (top, bottom)]
    xs, ys = zip(*corners)
    return (math.floor(min(xs)), math.floor(min(ys)),
            math.ceil(max(xs)), math.ceil(max(ys)))


def engine_color(color):
    channels = (color or {}).get("Values") or []
    if value((color or {}).get("Type")) != 1 or len(channels) < 4:
        raise ValueError("文字描边颜色不是 RGB")
    rgb = "#{:02X}{:02X}{:02X}".format(*(round(max(0, min(1, value(channel))) * 255)
                                            for channel in channels[1:4]))
    return rgb, max(0.0, min(1.0, float(value(channels[0]))))


def effect_color(effect):
    from psd_tools.terminology import Key

    color = effect.color
    if color is None:
        raise ValueError("PSD 描边效果缺少颜色")
    if Key.Red in color:
        channels = (float(color[key]) / 255 for key in (Key.Red, Key.Green, Key.Blue))
    elif Key.RedFloat in color:
        channels = (float(color[key]) for key in (Key.RedFloat, Key.GreenFloat, Key.BlueFloat))
    else:
        raise ValueError("PSD 描边效果颜色不是 RGB")
    return "#{:02X}{:02X}{:02X}".format(*(round(max(0, min(1, channel)) * 255)
                                             for channel in channels))


def text_outline(layer, style):
    effects = getattr(layer, "effects", None)
    if effects is not None:
        stroke = next(iter(effects.find("Stroke")), None)
        if stroke is not None and stroke.size > 0:
            if stroke.fill_type != b"SClr":
                raise ValueError(f"文字图层使用非纯色描边，无法表示为单色 outline：{layer.name}")
            return {"size": stroke.size, "color": effect_color(stroke),
                    "alpha": max(0.0, min(1.0, stroke.opacity / 100))}

    if value(style.get("StrokeFlag")):
        size = value(style.get("OutlineWidth", 0))
        if size > 0:
            color, alpha = engine_color(style.get("StrokeColor"))
            return {"size": size, "color": color, "alpha": alpha}
    return None


def text_properties(layer):
    """Read the first non-empty Photoshop text style run."""
    resources = layer.resource_dict or {}
    engine = layer.engine_dict or {}
    styles = engine.get("StyleRun") or {}
    runs = styles.get("RunArray") or []
    lengths = styles.get("RunLengthArray") or []
    run = next((item for length, item in zip(lengths, runs) if value(length) > 0),
               runs[0] if runs else None)
    if run is None:
        raise ValueError(f"文字图层缺少样式信息：{layer.name}")

    defaults = resources.get("StyleSheetSet") or []
    default_index = value(resources.get("TheNormalStyleSheet", 0))
    default_style = (defaults[default_index].get("StyleSheetData") or {}
                     if 0 <= default_index < len(defaults) else {})
    run_style = (run.get("StyleSheet") or {}).get("StyleSheetData") or {}
    fields = ("Font", "FontSize", "FillColor", "StrokeFlag", "OutlineWidth", "StrokeColor")
    style = {key: value(default_style.get(key)) for key in fields
             if default_style.get(key) is not None}
    style.update({key: value(run_style.get(key)) for key in fields
                  if run_style.get(key) is not None})

    fonts = resources.get("FontSet") or []
    font_index = style.get("Font")
    if not isinstance(font_index, int) or not 0 <= font_index < len(fonts):
        raise ValueError(f"文字图层缺少有效字体索引：{layer.name}")
    font_name = value(fonts[font_index].get("Name"))
    font_size = style.get("FontSize")
    if not isinstance(font_name, str) or not font_name or not isinstance(font_size, (int, float)) or font_size <= 0:
        raise ValueError(f"文字图层缺少字体名或字号：{layer.name}")

    # Apply the text layer's vertical scale; rotation does not affect size.
    _, _, yx, yy, _, _ = layer.transform
    font_size = max(1, math.floor(font_size * math.hypot(yx, yy) + 0.5))

    color = "#FFFFFF"
    fill = style.get("FillColor") or {}
    channels = fill.get("Values") or []
    if value(fill.get("Type")) == 1 and len(channels) >= 4:
        color = "#{:02X}{:02X}{:02X}".format(*(round(max(0, min(1, value(channel))) * 255)
                                              for channel in channels[1:4]))
    result = {"text": layer.text.rstrip("\r").replace("\r", "\n"), "fontName": font_name,
              "fontSize": font_size, "color": color}
    outline = text_outline(layer, style)
    if outline is not None:
        result["outline"] = outline
    return result


def compact_nine_slice(image):
    """Remove identical consecutive rows/columns; keep one stretch pixel and edges."""
    from PIL import Image

    image = image.convert("RGBA")

    def stretch_run(length, sample):
        # Keep at least one fixed pixel at either end for Unity's borders.
        best_start, best_end = 1, 2
        start = 1
        previous = sample(1) if length >= 3 else None
        for index in range(2, length - 1):
            current = sample(index)
            if current != previous:
                start = index
            if index + 1 - start > best_end - best_start:
                best_start, best_end = start, index + 1
            previous = current
        return (best_start, best_end) if length >= 3 else (0, length)

    width, height = image.size
    x0, x1 = stretch_run(width, lambda x: image.crop((x, 0, x + 1, height)).tobytes())
    y0, y1 = stretch_run(height, lambda y: image.crop((0, y, width, y + 1)).tobytes())
    xs = [(0, x0), (x0, min(x0 + 1, x1)), (x1, width)]
    ys = [(0, y0), (y0, min(y0 + 1, y1)), (y1, height)]
    # Tiny images cannot have a valid stretch region: retain that axis unchanged.
    if width < 3:
        xs = [(0, 0), (0, width), (width, width)]
    if height < 3:
        ys = [(0, 0), (0, height), (height, height)]
    result = Image.new("RGBA", (sum(b - a for a, b in xs), sum(b - a for a, b in ys)))
    dest_y = 0
    for top, bottom in ys:
        dest_x = 0
        for left, right in xs:
            if right > left and bottom > top:
                result.paste(image.crop((left, top, right, bottom)), (dest_x, dest_y))
            dest_x += right - left
        dest_y += bottom - top
    return result, {"left": x0, "bottom": height - y1, "right": width - x1, "top": y0}


def export(path, target, mode="all", green_only=False, seen_images=None, seen_pages=None):
    try:
        from psd_tools import PSDImage
    except ImportError as error:
        raise RuntimeError("缺少 psd-tools：请为 Unity 使用的 Python 安装 psd-tools") from error

    psd = PSDImage.open(path)
    target.mkdir(parents=True, exist_ok=True)
    exported = 0
    seen_images = set() if seen_images is None else seen_images
    seen_pages = set() if seen_pages is None else seen_pages
    pages = []
    origin_x = origin_y = 0
    assets_root = next((parent for parent in (target, *target.parents) if parent.name == "Assets"), None)
    if assets_root is None:
        raise ValueError("输出目录必须位于 Assets 内")


    def convert(layer):
        nonlocal exported
        node_type = "container" if layer.is_group() else "text" if layer.kind == "type" else "image"
        left, top, right, bottom = text_bounds(layer) if node_type == "text" and mode != "images" else layer.bbox
        node = {"name": layer.name or "layer", "type": node_type, "isHidden": not layer.visible,
                "x": left - origin_x, "y": top - origin_y, "w": right - left, "h": bottom - top}
        if layer.is_group():
            node["children"] = [child for item in reversed(list(layer)) for child in [convert(item)] if child is not None]
        elif node_type == "text" and mode != "images":
            node.update(text_properties(layer))
        if node["type"] == "image":
            filename = f"{safe_name(layer.name)}.png"
            node["sprite"] = filename
            if green_only:
                node["hasGreenTag"] = has_green_label(layer)
            if mode != "structure" and right > left and bottom > top and (not green_only or has_green_label(layer)):
                key = filename.casefold()
                if key in seen_images:
                    print(f"跳过同名图片：{filename}（{page_name} / {layer.name}）")
                else:
                    image = layer.topil()
                    if image is None:
                        image = layer.composite(force=True, layer_filter=lambda item: True)
                    if image is None:
                        raise ValueError(f"图层无法渲染：{layer.name}")
                    folder = "Big" if Path(filename).stem.lower().endswith("_bg") else "Main"
                    image_dir = target / "Textures" / folder
                    image_dir.mkdir(parents=True, exist_ok=True)
                    image_path = image_dir / filename
                    png_info = None
                    if Path(filename).stem.lower().endswith("_9s"):
                        original_size = image.size
                        image, border = compact_nine_slice(image)
                        from PIL.PngImagePlugin import PngInfo
                        png_info = PngInfo()
                        png_info.add_text("UnityNineSlice", json.dumps(border))
                        print(f"九宫格：{filename} {original_size} -> {image.size}")
                    image.save(image_path, pnginfo=png_info)
                    seen_images.add(key)
                    exported += 1
        return node

    json_dir = target / "Config" / "psd"
    for page_name, container, bounds, hidden in export_pages(psd, path):
        key = page_name.casefold()
        if key in seen_pages:
            print(f"跳过同名画板：{page_name}（{path.name}）")
            continue
        seen_pages.add(key)
        origin_x, origin_y, right, bottom = bounds
        layout = {"name": page_name, "width": right - origin_x, "height": bottom - origin_y,
                  "isHidden": hidden,
                  "children": [convert(layer) for layer in reversed(list(container))]}
        pages.append(page_name)
        if mode != "images":
            json_dir.mkdir(parents=True, exist_ok=True)
            json_path = json_dir / f"{page_name}.json"
            temporary = json_path.with_suffix(".json.part")
            temporary.write_text(json.dumps(layout, ensure_ascii=False, indent=2), encoding="utf-8")
            temporary.replace(json_path)
            print(f"结构导出完成：{json_path}")
    if mode != "images":
        manifest_dir = json_dir / "Manifests"
        manifest_dir.mkdir(parents=True, exist_ok=True)
        manifest_path = manifest_dir / f"{path.stem}.json"
        temporary = manifest_path.with_suffix(".json.part")
        temporary.write_text(json.dumps({"pages": pages}, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(manifest_path)
    print(f"PSD 导出完成：{len(pages)} 个画板，{exported} 张切图")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, help="相对项目根目录的 PSD 路径")
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--assets-path", required=True, help="以 Assets/ 开头的项目内输出目录")
    parser.add_argument("--mode", choices=("all", "structure", "images"), default="all")
    parser.add_argument("--green-only", action="store_true", help="标记图片绿色标签并只导出绿色图片")
    args = parser.parse_args()
    try:
        paths = source_files(args.project_root, args.source)
        target = output_directory(args.project_root, args.assets_path)
        seen_images, seen_pages = set(), set()
        for path in paths:
            export(path, target, args.mode, args.green_only, seen_images, seen_pages)
    except (ValueError, RuntimeError) as error:
        print(f"错误：{error}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
