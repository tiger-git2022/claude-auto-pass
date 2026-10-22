#!/usr/bin/env bash
set -euo pipefail
mkdir -p outputs/rule-tests
javac -encoding UTF-8 -d outputs/rule-tests app/src/main/java/com/personal/claudeautopass/ApprovalRules.java tests/ApprovalRulesTest.java app/src/main/java/com/personal/claudeautopass/Versioning.java tests/VersioningTest.java
java -cp outputs/rule-tests com.personal.claudeautopass.ApprovalRulesTest

java -cp outputs/rule-tests com.personal.claudeautopass.VersioningTest
