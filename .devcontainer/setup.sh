#!/usr/bin/env bash
# Runs once when the dev container is created: installs the tools the local
# dev/E2E scripts need beyond what the base image and features provide.
set -euo pipefail

echo "Installing Azure Functions Core Tools..."
wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y azure-functions-core-tools-4

echo "Installing Azurite and the Static Web Apps CLI..."
npm install -g azurite@^3.37.0 @azure/static-web-apps-cli

echo "Restoring and building the .NET solution..."
dotnet restore
dotnet build --configuration Debug

if [ -f tests/package.json ]; then
  echo "Installing Playwright test dependencies..."
  (cd tests && npm ci && npx playwright install --with-deps chromium)
fi

chmod +x scripts/*.sh

echo ""
echo "Setup complete."
echo "  Start local dev: scripts/start-local.sh"
echo "  Stop local dev:   scripts/stop-local.sh"
echo "  Run E2E tests:    scripts/start-e2e.sh && (cd tests && npm test)"
