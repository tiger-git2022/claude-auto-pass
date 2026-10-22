import os, pathlib, re, subprocess

def git(*args):
    return subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()

build_file = pathlib.Path('app/build.gradle.kts').read_text(encoding='utf-8-sig')
base = re.search(r'val baseVersionName = "([0-9.]+)"', build_file).group(1)
base_code = int(re.search(r'val baseVersionCode = ([0-9]+)', build_file).group(1))
sha = os.environ['GITHUB_SHA']
publish = os.environ.get('PUBLISH') == 'true'
force = os.environ.get('FORCE') == 'true'
existing = git('tag', '--points-at', sha, '--list', f'v{base}-t*')
tags = git('tag', '--list', f'v{base}-t*').splitlines()
numbers = [int(tag.rsplit('-t', 1)[1]) for tag in tags if re.fullmatch(r'v' + re.escape(base) + r'-t\d+', tag)]
number = max(numbers, default=0) + 1
if number > 999:
    raise SystemExit('Increment baseVersionCode and baseVersionName before build 1000')
skip = bool(publish and existing and not force)
outputs = {'skip': str(skip).lower(), 'build': str(number), 'tag': f'v{base}-t{number}', 'base': base,
           'version_code': str(base_code * 1000 + number), 'publish': str(publish).lower()}
with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as out:
    for key, value in outputs.items():
        out.write(f'{key}={value}\n')
print('Already released; skipping.' if skip else f'Build {outputs["tag"]}, publish={publish}')
