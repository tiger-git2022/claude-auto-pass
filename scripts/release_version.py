"""Release versions shared by the APK (tags vX.Y.Z) and the Windows exe (tags win-vX.Y.Z).

The project file holds the base version X.Y.Z, raised by hand together with the changelog: a new feature raises
Y, a big change X, a fix may raise Z. A push that doesn't raise it is released as the next unused Z, so every
push to main still gets its own release. Before November 2026 tags were <prefix>X.Y.Z-tN; their Z counts as
taken too. A base whose X.Y is below the newest release is refused.
"""
import re


def parse(tag, prefix):
    """(X, Y, Z, N) of a tag with this prefix, N = 0 without "-tN"; None for other tags."""
    m = re.fullmatch(re.escape(prefix) + r'(\d+)\.(\d+)\.(\d+)(?:-t(\d+))?', tag)
    return tuple(int(g or 0) for g in m.groups()) if m else None


def next_version(base, prefix, tags):
    """The version to release for base X.Y.Z, given the existing tags."""
    major, minor, patch = (int(p) for p in base.split('.'))
    released = [v for v in (parse(t, prefix) for t in tags) if v]
    taken = [v[2] for v in released if v[:2] == (major, minor)]
    if taken and max(taken) >= patch:
        patch = max(taken) + 1
    version = (major, minor, patch, 0)
    if released and version <= max(released):
        newest = max(released)
        raise SystemExit(f'{prefix}{major}.{minor}.{patch} would not be newer than '
                         f'{prefix}{newest[0]}.{newest[1]}.{newest[2]}' + (f'-t{newest[3]}' if newest[3] else '')
                         + '; raise the base version')
    return f'{major}.{minor}.{patch}'


def version_code(version):
    major, minor, patch = (int(p) for p in version.split('.'))
    return major * 10_000_000 + minor * 10_000 + patch
