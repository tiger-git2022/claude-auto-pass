import os, pathlib, re, subprocess, sys
base, tag, code = sys.argv[1:]
changelog = pathlib.Path('CHANGELOG.md').read_text(encoding='utf-8-sig')
match = re.search(r'^## ' + re.escape(base) + r'\s*\n(.*?)(?=^## |\Z)', changelog, re.M | re.S)
if not match:
    sys.exit('Missing version notes in CHANGELOG.md')
notes = match.group(1).strip()
if not ('### 中文' in notes and '### English' in notes and notes.index('### 中文') < notes.index('### English')):
    sys.exit('Release notes must contain Chinese first, then English')
zh, en = notes.split('### English', 1)
previous = subprocess.run(['git', 'describe', '--tags', '--abbrev=0', '--match', 'v*-t*', 'HEAD'], capture_output=True, text=True).stdout.strip()
log_range = [f'{previous}..HEAD'] if previous else ['-n', '20', 'HEAD']
subjects = subprocess.check_output(['git', 'log', '--first-parent', '--format=%s (%h)', *log_range, '--', '.', ':(exclude)windows'], text=True, encoding='utf-8').strip().splitlines()
zh_changes, en_changes = [], []
for subject in subjects:
    parts = subject.split(' / ', 1)
    zh_changes.append('- ' + parts[0])
    en_changes.append('- ' + (parts[1] if len(parts) == 2 else parts[0]))
body = zh.strip() + '\n\n#### 本次提交\n' + '\n'.join(zh_changes)
body += f'\n\n#### 下载\n下载 `claude-auto-pass.apk`。包名 `com.personal.claudeautopass`，同签名可覆盖安装。versionCode {code}。\n\n---\n\n'
body += '### English\n' + en.strip() + '\n\n#### Changes in this build\n' + '\n'.join(en_changes)
body += f'\n\n#### Download\nInstall `claude-auto-pass.apk`. Package `com.personal.claudeautopass`; the same signing key supports in-place updates. versionCode {code}.\n'
pathlib.Path('release-notes.md').write_text(body, encoding='utf-8')
