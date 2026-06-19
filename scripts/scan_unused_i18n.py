"""扫描 i18n 翻译键，找出未被任何代码引用的键"""

import re
import os
import sys

def parse_yml_keys(yml_path):
    """解析 yml 文件，提取所有翻译键（支持嵌套点号格式）"""
    keys = []
    prefix = []
    
    with open(yml_path, 'r', encoding='utf-8') as f:
        for line in f:
            stripped = line.rstrip()
            
            # 跳过空行和注释行
            if not stripped or stripped.lstrip().startswith('#'):
                continue
            
            # 计算缩进层级
            indent = len(line) - len(line.lstrip())
            
            # 解析 key: value
            match = re.match(r'^(\s*)([\w.-]+)\s*:\s*(.+)$', stripped)
            if not match:
                continue
            
            key = match.group(2)
            level = indent // 2  # 假设每级2空格
            
            # 调整前缀层级
            prefix = prefix[:level]
            
            # 构建完整键名
            if prefix:
                full_key = '.'.join(prefix) + '.' + key
            else:
                full_key = key
            
            keys.append(full_key)
            
            # 如果值是空的或包含换行块，说明是父级
            value = match.group(3).strip()
            if value in ('', '|', '>'):
                prefix.append(key)
    
    return keys


def find_unused_keys(yml_path, search_dirs, extensions):
    """查找未被引用的翻译键"""
    # 解析所有键
    all_keys = parse_yml_keys(yml_path)
    print(f"共找到 {len(all_keys)} 个翻译键")
    
    # 读取所有源文件内容
    file_contents = []
    for search_dir in search_dirs:
        if not os.path.exists(search_dir):
            print(f"  跳过不存在的目录: {search_dir}")
            continue
        for root, dirs, files in os.walk(search_dir):
            # 跳过 node_modules, bin, obj, .git
            dirs[:] = [d for d in dirs if d not in ('node_modules', 'bin', 'obj', '.git', 'dist', '.next')]
            for fname in files:
                if any(fname.endswith(ext) for ext in extensions):
                    fpath = os.path.join(root, fname)
                    try:
                        with open(fpath, 'r', encoding='utf-8', errors='ignore') as f:
                            content = f.read()
                        file_contents.append((fpath, content))
                    except Exception as e:
                        print(f"  读取失败: {fpath} - {e}")
    
    print(f"扫描了 {len(file_contents)} 个源文件")
    
    # 检查每个键是否被引用
    unused_keys = []
    for key in all_keys:
        found = False
        # 搜索键名（作为字符串出现即可）
        # 转义点号用于正则
        pattern = re.escape(key)
        for fpath, content in file_contents:
            if re.search(pattern, content):
                found = True
                break
        if not found:
            unused_keys.append(key)
    
    return unused_keys


def main():
    # 项目根目录
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.abspath(os.path.join(script_dir, '..'))
    
    yml_path = os.path.join(project_root, 'MorningCat.I18n', 'Lang', 'zh.yml')
    
    # 搜索目录
    search_dirs = [
        os.path.join(project_root, 'MorningCat'),
        os.path.join(project_root, 'MorningCat.WebUI'),
        os.path.join(project_root, 'MorningCat.I18n'),
        os.path.join(project_root, 'MorningCat.PluginAPI'),
        os.path.join(project_root, 'MorningCat.PlatformAbstraction'),
        os.path.join(project_root, 'MorningCat.Security'),
        os.path.join(project_root, 'webui', 'src'),
        os.path.join(project_root, '定制插件'),
    ]
    
    # 搜索的文件扩展名
    extensions = ('.cs', '.tsx', '.ts', '.jsx', '.js', '.xaml')
    
    print(f"项目根目录: {project_root}")
    print(f"翻译文件: {yml_path}")
    print(f"搜索目录: {search_dirs}")
    print(f"文件扩展名: {extensions}")
    print()
    
    unused = find_unused_keys(yml_path, search_dirs, extensions)
    
    print()
    print(f"=== 未被引用的翻译键 ({len(unused)} 个) ===")
    for key in sorted(unused):
        print(f"  {key}")
    
    if not unused:
        print("  (无)")


if __name__ == '__main__':
    main()
