#!/bin/bash
# Full verification sweep, for a Linux dev box.
#
# WPF/XAML cannot be compiled on Linux (Ubuntu's SDK omits the WindowsDesktop
# targets), so instead we: build the Core library, type-check the ViewModels
# against real WPF reference assemblies, statically validate the XAML bindings,
# and run the Core test suite.
#
# Run this before every commit. On Windows, `dotnet build` on the solution
# covers steps 1-3 properly and this script is unnecessary.
set -o pipefail
TOOLS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(dirname "$TOOLS")"
FAIL=0

echo "=== 1/4 build Core ==="
dotnet build "$REPO/src/DiagFileMonitor.Core/DiagFileMonitor.Core.csproj" -v q --nologo 2>&1 \
  | grep -E "error|Warning\(s\)|Error\(s\)|Build succeeded" | head -20
if [ ${PIPESTATUS[0]} -ne 0 ]; then echo ">>> STEP FAILED <<<"; FAIL=1; fi

echo "=== 2/4 type-check ViewModels against WPF refs ==="
dotnet build "$TOOLS/vmcheck/vmcheck.csproj" -v q --nologo 2>&1 \
  | grep -E "error|Build succeeded" | head -20
if [ ${PIPESTATUS[0]} -ne 0 ]; then echo ">>> STEP FAILED <<<"; FAIL=1; fi

echo "=== 3/4 XAML binding check ==="
(cd "$TOOLS" && python3 check_xaml.py) || { echo ">>> STEP FAILED <<<"; FAIL=1; }

echo "=== 4/4 Core tests ==="
dotnet test "$REPO/tests/DiagFileMonitor.Core.Tests/DiagFileMonitor.Core.Tests.csproj" -v q --nologo 2>&1 \
  | grep -E "error|Passed!|Failed!|Passed:|Failed:" | head -20
if [ ${PIPESTATUS[0]} -ne 0 ]; then echo ">>> STEP FAILED <<<"; FAIL=1; fi

echo
[ $FAIL -eq 0 ] && echo "ALL CHECKS PASSED" || echo "CHECKS FAILED"
exit $FAIL
