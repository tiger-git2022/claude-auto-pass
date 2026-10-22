import json, os, pathlib, shutil, subprocess, tempfile
root = pathlib.Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='claude-auto-pass-ci-') as directory:
    fixture = pathlib.Path(directory)
    (fixture / 'app').mkdir()
    shutil.copyfile(root / 'app/build.gradle.kts', fixture / 'app/build.gradle.kts')
    shutil.copyfile(root / 'CHANGELOG.md', fixture / 'CHANGELOG.md')
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=fixture, text=True, encoding='utf-8', stderr=subprocess.DEVNULL).strip()
    git('init', '-b', 'main')
    git('config', 'user.name', 'CI Test')
    git('config', 'user.email', 'test@example.invalid')
    git('add', '.')
    git('commit', '-m', '首次发布 / Initial release')
    def version(force='false'):
        output = fixture / 'output.txt'
        output.write_text('', encoding='utf-8')
        env = dict(os.environ, GITHUB_SHA=git('rev-parse', 'HEAD'), GITHUB_OUTPUT=str(output), PUBLISH='true', FORCE=force)
        subprocess.run(['python', str(root / 'scripts/ci-version.py')], cwd=fixture, env=env, check=True, capture_output=True)
        return dict(line.split('=', 1) for line in output.read_text().splitlines())
    initial = version()
    assert initial['tag'] == 'v1.0.0-t1' and initial['version_code'] == '1001' and initial['skip'] == 'false'
    git('tag', 'v1.0.0-t1')
    assert version()['skip'] == 'true'
    assert version('true')['tag'] == 'v1.0.0-t2' and version('true')['skip'] == 'false'
    (fixture / 'second.txt').write_text('second', encoding='utf-8')
    git('add', '.')
    git('commit', '-m', '改进按钮 / Improve buttons')
    second = version()
    assert second['tag'] == 'v1.0.0-t2' and second['version_code'] == '1002'
    subprocess.run(['python', str(root / 'scripts/release-notes.py'), '1.0.0', second['tag'], second['version_code']], cwd=fixture, check=True)
    notes = (fixture / 'release-notes.md').read_text(encoding='utf-8')
    assert notes.index('### 中文') < notes.index('### English')
    assert '改进按钮' in notes[:notes.index('### English')]
    assert 'Improve buttons' in notes[notes.index('### English'):]
    assert '1002' in notes and 'claude-auto-pass.apk' in notes
print('Passed release numbering, repeat-run, and bilingual notes checks.')
