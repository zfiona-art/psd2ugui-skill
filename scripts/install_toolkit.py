#!/usr/bin/env python3
"""Install bundled PSD UGUI sources into a Unity project without overwriting edits."""
import argparse
import json
import shutil
from pathlib import Path


def install(project, dry_run=False):
    project = Path(project).expanduser().resolve()
    if not (project / 'Assets').is_dir() or not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('目标不是 Unity 项目：需要 Assets 和 ProjectSettings/ProjectVersion.txt')
    manifest = project / 'Packages/manifest.json'
    dependencies = json.loads(manifest.read_text(encoding='utf-8')).get('dependencies', {}) if manifest.exists() else {}
    if 'com.unity.ugui' not in dependencies:
        raise ValueError('请先通过目标 Unity 版本的 Package Manager 添加 com.unity.ugui，再接入工具')
    source = Path(__file__).resolve().parent.parent / 'assets/toolkit'
    files = sorted(p for p in source.rglob('*') if p.is_file())
    pending = []
    for src in files:
        relative = src.relative_to(source)
        dest = project / relative
        # Disallow symlink escapes even when a parent folder already exists.
        if project not in dest.resolve().parents:
            raise ValueError(f'目标路径超出项目：{relative}')
        if dest.exists():
            if not dest.is_file() or dest.read_bytes() != src.read_bytes():
                raise ValueError(f'已有不同内容，未写入任何文件：{relative}')
        else:
            pending.append((src, dest))
    for src, dest in pending:
        print(('计划新增：' if dry_run else '新增：') + str(dest.relative_to(project)))
    if not dry_run:
        for src, dest in pending:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dest)
    print(f'{"预检完成" if dry_run else "接入完成"}：新增 {len(pending)}，已有相同文件 {len(files)-len(pending)}。')
    print('下一步：准备 Python 依赖，返回 Unity 等待编译，打开 Tools/PSD UGUI/Export。')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project-root', required=True)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    try:
        install(args.project_root, args.dry_run)
    except (ValueError, OSError, json.JSONDecodeError) as error:
        parser.exit(1, f'错误：{error}\n')


if __name__ == '__main__':
    main()
