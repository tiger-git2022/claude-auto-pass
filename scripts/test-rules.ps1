$ErrorActionPreference = 'Stop'
$taskJdk = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'C:\Program Files\Android\Android Studio\jbr' }
New-Item -ItemType Directory -Force -Path 'outputs/rule-tests' | Out-Null
& "$taskJdk/bin/javac.exe" -encoding UTF-8 -d outputs/rule-tests app/src/main/java/com/personal/claudeautopass/ApprovalRules.java tests/ApprovalRulesTest.java app/src/main/java/com/personal/claudeautopass/Versioning.java tests/VersioningTest.java
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& "$taskJdk/bin/java.exe" -cp outputs/rule-tests com.personal.claudeautopass.ApprovalRulesTest
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& "$taskJdk/bin/java.exe" -cp outputs/rule-tests com.personal.claudeautopass.VersioningTest
exit $LASTEXITCODE
