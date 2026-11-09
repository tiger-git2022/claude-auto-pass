import os, pathlib, re, subprocess
from release_version import next_version, version_code

def git(*args):
    return subprocess.check_output(['git', *args], text=True, encoding='utf-8').strip()

build_file = pathlib.Path('app/build.gradle.kts').read_text(encoding='utf-8-sig')
base = re.search(r'val baseVersionName = "([0-9]+\.[0-9]+\.[0-9]+)"', build_file).group(1)
sha = os.environ['GITHUB_SHA']
publish = os.environ.get('PUBLISH') == 'true'
force = os.environ.get('FORCE') == 'true'
existing = [t for t in git('tag', '--points-at', sha, '--list', 'v*').splitlines() if re.fullmatch(r'v\d+\.\d+\.\d+(-t\d+)?', t)]
version = next_version(base, 'v', git('tag', '--list', 'v*').splitlines())
skip = bool(publish and existing and not force)
outputs = {'skip': str(skip).lower(), 'version': version, 'tag': f'v{version}', 'base': base,
           'version_code': str(version_code(version)), 'publish': str(publish).lower()}
with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as out:
    for key, value in outputs.items():
        out.write(f'{key}={value}\n')
print('Already released; skipping.' if skip else f'Build {outputs["tag"]}, publish={publish}')
