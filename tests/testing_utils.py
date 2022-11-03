#
# Copyright (c) nexB Inc. and others. All rights reserved.
# ScanCode is a trademark of nexB Inc.
# SPDX-License-Identifier: Apache-2.0
# See http://www.apache.org/licenses/LICENSE-2.0 for the license text.
# See https://github.com/nexB/nuget-inspector for support or download.
# See https://aboutcode.org for more information about nexB OSS projects.
#

import os
from pathlib import Path

# Used for tests to regenerate fixtures with regen=True
REGEN_TEST_FIXTURES = os.getenv("REGEN_TEST_FIXTURES", False)
ROOT_DIR = Path(__file__).parent.parent.absolute()
TEST_DATA_DIR = ROOT_DIR / "tests" / "data"

