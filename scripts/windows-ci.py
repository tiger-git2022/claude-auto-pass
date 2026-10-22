"""Version numbers and release notes for the Windows exe (tags win-v<base>-tN, separate from the APK's v<base>-tN)."""
import os, pathlib, re, subprocess, sys

PATHS = ['windows', '.github/workflows/windows.yml', 'scripts/windows-ci.py']


def git(*args):
    return subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()


def base_version():
    project = pathlib.Path('windows/ClaudeAutoPass.csproj').read_text(encoding='utf-8-sig')
    return re.search(r'<BaseVersion>([0-9]+\.[0-9]+\.[0-9]+)</BaseVersion>', project).group(1)


def version():
    base = base_version()
    sha = os.environ['GITHUB_SHA']
    publish = os.environ.get('PUBLISH') == 'true'
    force = os.environ.get('FORCE') == 'true'
    existing = git('tag', '--points-at', sha, '--list', f'win-v{base}-t*')
    pattern = re.compile(r'win-v' + re.escape(base) + r'-t(\d+)')
    numbers = [int(m.group(1)) for t in git('tag', '--list', f'win-v{base}-t*').splitlines() if (m := pattern.fullmatch(t))]
    number = max(numbers, default=0) + 1
    skip = bool(publish and existing and not force)
    outputs = {'skip': str(skip).lower(), 'build': str(number), 'tag': f'win-v{base}-t{number}', 'base': base,
               'publish': str(publish).lower()}
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as out:
        for key, value in outputs.items():
            out.write(f'{key}={value}\n')
    print('Already released; skipping.' if skip else f'Build {outputs["tag"]}, publish={publish}')


def notes(base, tag):
    changelog = pathlib.Path('windows/CHANGELOG.md').read_text(encoding='utf-8-sig')
    match = re.search(r'^## ' + re.escape(base) + r'\s*\n(.*?)(?=^## |\Z)', changelog, re.M | re.S)
    if not match:
        sys.exit('Missing version notes in windows/CHANGELOG.md')
    text = match.group(1).strip()
    if not ('### 中文' in text and '### English' in text and text.index('### 中文') < text.index('### English')):
        sys.exit('Release notes must contain Chinese first, then English')
    zh, en = text.split('### English', 1)
    previous = subprocess.run(['git', 'describe', '--tags', '--abbrev=0', '--match', 'win-v*-t*', 'HEAD'],
                              capture_output=True, text=True).stdout.strip()
    log_range = [f'{previous}..HEAD'] if previous else ['-n', '20', 'HEAD']
    subjects = git('log', '--first-parent', '--format=%s (%h)', *log_range, '--', *PATHS).splitlines()
    zh_changes, en_changes = [], []
    for subject in subjects:
        parts = subject.split(' / ', 1)
        zh_changes.append('- ' + parts[0])
        en_changes.append('- ' + (parts[1] if len(parts) == 2 else parts[0]))
    body = zh.replace('### 中文', '### 中文（Windows 版）', 1).strip()
    body += '\n\n#### 本次提交\n' + '\n'.join(zh_changes or ['- 重新发布'])
    body += f'\n\n#### 下载\n下载 `claude-auto-pass.exe`，双击运行即可，无需安装（Windows 10/11 自带 .NET Framework 4.8）。版本 {tag}。\n\n---\n\n'
    body += '### English (Windows)\n' + en.strip()
    body += '\n\n#### Changes in this build\n' + '\n'.join(en_changes or ['- Rebuild'])
    body += f'\n\n#### Download\nDownload `claude-auto-pass.exe` and run it; no installer needed (Windows 10/11 include .NET Framework 4.8). Version {tag}.\n'
    pathlib.Path('windows-release-notes.md').write_text(body, encoding='utf-8')


if __name__ == '__main__':
    if sys.argv[1] == 'version':
        version()
    elif sys.argv[1] == 'notes':
        notes(sys.argv[2], sys.argv[3])
    else:
        sys.exit('usage: windows-ci.py version | notes <base> <tag>')
