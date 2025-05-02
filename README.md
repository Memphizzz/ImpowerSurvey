# ImpowerSurvey

A privacy-focused survey tool implementing the SHIELD principle (Separate Human Identities Entirely from Linked Data) to protect participant anonymity.

## Overview

ImpowerSurvey is designed to collect accurate survey feedback while maintaining complete separation between participant identities and their responses. The system uses a double-blind architecture with unique entry and completion codes to ensure anonymity while still tracking participation.

## Core Features

- Complete anonymity for survey participants
- Double-blind system using entry and completion codes
- Delayed submission service to further enhance anonymity
- Slack integration for distributing entry codes
- SHIELD compliance verification throughout the application
- Mobile-friendly UI
- Administrative dashboard for survey management
- AI-powered response anonymization and survey summarization
- Claude AI integration for intelligent data analysis

## SHIELD Principle

The SHIELD principle (Separate Human Identities Entirely from Linked Data) is the foundational privacy concept for ImpowerSurvey. It guarantees that:

- Participant identities are never connected to their responses
- Personal information is stored separately from survey data
- Unique codes facilitate anonymous participation tracking
- All designs and features maintain strict separation of identity and content

For more details, see the documentation in the `docs` folder, including the [Release Notes](docs/RELEASE_NOTES_1.0.0.md).

## Environment Variables

The following environment variables are available:

| Variable | Description | Example |
|----------|-------------|---------|
| `IS_CONNECTION_STRING` | PostgreSQL connection string | `Host=...;Database=...;Username=...;Password=...;` or `postgresql://username:password@host:port/database` |
| `IS_HOST_URL` | Application host URL | `https://app.example.com` |
| `IS_SLACK_API_TOKEN` | Slack API token for integration (optional) | `xoxb-...` |
| `IS_SLACK_APP_LEVEL_TOKEN` | Slack app-level token (optional) | `xapp-...` |
| `IS_CLAUDE_API_KEY` | Anthropic Claude API key (optional) | `sk-ant-...` |
| `IS_CLAUDE_MODEL` | Claude model to use (optional) | `claude-3-7-sonnet-20250219` |
| `IS_COOKIES_SECRET` | Secret for cookie encryption | `random-strong-secret-key` |
| `IS_INSTANCE_SECRET` | Secret for inter-instance communication | `random-strong-secret-key` |
| `IS_HOSTNAME` | Server hostname (optional, defaults to HOSTNAME) | `app-name-a1b2c3` |
| `IS_PORT` | Server port (optional, defaults to PORT) | `8080` |
| `IS_SCALE_OUT` | Enable multi-instance mode (optional, defaults to false) | `true` |

## Local Development Setup

### Prerequisites

- .NET 8 SDK
- PostgreSQL database
- Visual Studio 2022 or JetBrains Rider

### Setup Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/impower-ai/ImpowerSurvey.git
   cd ImpowerSurvey
   ```

2. Set up user secrets for local development:
   ```bash
   dotnet user-secrets init --project ImpowerSurvey
   dotnet user-secrets set "IS_CONNECTION_STRING" "your-connection-string" --project ImpowerSurvey
   dotnet user-secrets set "IS_HOST_URL" "https://localhost:5003" --project ImpowerSurvey
   dotnet user-secrets set "IS_SLACK_API_TOKEN" "your-slack-api-token" --project ImpowerSurvey
   dotnet user-secrets set "IS_SLACK_APP_LEVEL_TOKEN" "your-slack-app-level-token" --project ImpowerSurvey
   dotnet user-secrets set "IS_COOKIES_SECRET" "your-cookie-secret" --project ImpowerSurvey
   dotnet user-secrets set "IS_CLAUDE_API_KEY" "your-claude-api-key" --project ImpowerSurvey
   dotnet user-secrets set "IS_CLAUDE_MODEL" "claude-3-7-sonnet-20250219" --project ImpowerSurvey
   dotnet user-secrets set "IS_INSTANCE_SECRET" "your-instance-secret" --project ImpowerSurvey
   dotnet user-secrets set "IS_SCALE_OUT" "true" --project ImpowerSurvey
   ```

3. Update `launchSettings.json` with your local IP address for testing:
   ```bash
   # After editing, consider:
   git update-index --assume-unchanged ImpowerSurvey/Properties/launchSettings.json
   ```

4. Build and run the project:
   ```bash
   dotnet build
   dotnet run --project ImpowerSurvey
   ```

5. Access the application at `https://localhost:5003` (or your configured URL)

### Initial Login

On first run, a default admin account is created:
- Username: `ImpowerAdmin`
- Password: `change!me`

You will be required to change this password on first login.

### Multi-Instance Setup

For multi-instance deployments:

1. All instances must have access to the same PostgreSQL database.
2. The `IS_INSTANCE_SECRET` environment variable must be set to the same value across all instances.
3. The `IS_HOSTNAME` and `IS_PORT` environment variables are used to create a unique instance ID (`IS_HOSTNAME:IS_PORT`). These default to standard Azure `HOSTNAME` and `PORT` if not explicitly set.
4. Set `IS_SCALE_OUT=true` to enable multi-instance mode.
5. Ensure instances can communicate with each other via HTTP. The LeaderElectionService will verify this connectivity at startup to prevent data loss.

> **Note for Azure App Service users**: Azure App Service does not support direct communication between scaled instances ("east-west" communication). For multi-instance deployments, consider using Railway or another platform that supports direct container-to-container communication.

#### Single-Instance Mode vs. Multi-Instance Mode

The application supports two operational modes:

**Single-Instance Mode** (`IS_SCALE_OUT=false` or not set):
- The instance automatically becomes the leader without database checks
- No periodic database queries for leader election
- Reduced database write operations and overall database strain
- Ideal for development or vertically scaling scenarios (scale up)

**Multi-Instance Mode** (`IS_SCALE_OUT=true`):
- Implements leader election through database coordination
- One instance becomes the leader to handle responses and scheduled tasks
- Leader-follower architecture with inter-instance communication
- Followers forward responses to leader via internal API
- Inter-instance data transfer with secret authentication using `IS_INSTANCE_SECRET`
- Startup verification ensures all instances can communicate with the leader

The leader election mechanism ensures consistency in a multi-instance environment while maintaining SHIELD privacy compliance.

## Deployment

### Railway (Recommended)

Railway is the preferred deployment platform for ImpowerSurvey, offering native support for container-to-container communication and serverless PostgreSQL:

1. Click the deploy button below to create a new Railway project with ImpowerSurvey:

   [![Deploy on Railway](https://railway.com/button.svg)](https://railway.com/template/1tY47M?referralCode=6X4KFq)

2. The template automatically sets up:
   - The ImpowerSurvey web application
   - A PostgreSQL database
   - All required environment variables

3. Once deployed, Railway will provide a public URL for your application

### GitHub Actions (Alternative)

The application also supports deployment via GitHub Actions workflows:

#### Build Process (Automatic)

The build workflow, defined in `.github/workflows/build_impower-survey.yml`, runs automatically on push to the main branch:

1. Builds the application
2. Runs all unit tests
3. Creates and uploads the deployment artifact
4. **Does not automatically deploy** to ensure DSS safety

#### Deployment Process (Manual)

The deployment workflow, defined in `.github/workflows/deploy_impower-survey.yml`, must be triggered manually:

1. Requires explicit confirmation that all surveys are stopped and the DSS queue is empty
2. Downloads the latest successful build artifact
3. Authenticates with the hosting platform
4. Deploys to the production environment

This two-stage approach ensures that new deployments only happen after verifying that no survey data is at risk of being lost.

### Azure Configuration

For Azure App Service deployment, ensure all required environment variables are configured in your App Service settings:

- All environment variables listed in the "Environment Variables" section
- Set `IS_SCALE_OUT` to `false` for single-instance Azure deployments (multi-instance is not supported due to inter-instance communication limitations)

**CRITICAL HOSTING REQUIREMENTS:**

1. **Minimum Instance Count = 1**: Ensure at least one instance is running at all times. While multiple instances can work together through the leader election system, the application must **never** scale to zero instances.

2. **Prevent Application Shutdown**: Configure your hosting environment to prevent automatic shutdown due to inactivity.

   **WARNING**: If the application is shut down or restarted, all in-memory pending responses in the Delayed Submission Service (DSS) queue will be permanently lost. This is by design and a key part of the SHIELD privacy architecture, but means proper shutdown procedures must be followed to avoid data loss.

### Shutdown Procedure

**IMPORTANT:** Before shutting down, restarting, or deploying to the server:

1. Ensure all active surveys are properly closed
2. Wait for the Delayed Submission Service to process all pending responses
3. Check the Admin → DSS Status page to confirm all responses have been flushed
4. Only after confirming the DSS is empty should you proceed with deployment

Failure to follow this procedure may result in the loss of survey responses that are still in the submission. This is an intentional security feature to prevent administrators from correlating individual responses with participant identities.

## Testing

### Running Tests

Run the test suite with:
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Testing Different Modes

1. **Multi-Instance Mode Testing**:
   ```bash
   dotnet test --settings ImpowerSurvey.Tests/ImpowerSurvey.MultiInstanceMode.runsettings --filter "TestCategory=MultiInstanceMode"
   ```

2. **Single-Instance Mode Testing**:
   ```bash
   dotnet test --settings ImpowerSurvey.Tests/ImpowerSurvey.SingleInstanceMode.runsettings --filter "TestCategory=SingleInstanceMode"
   ```

3. **Regular Tests** (excluding instance mode-specific tests):
   ```bash
   dotnet test --filter "TestCategory!=MultiInstanceMode&TestCategory!=SingleInstanceMode"
   ```

The `.runsettings` files are necessary because the LeaderElectionService uses static readonly fields that are initialized when the class is first loaded. Since these fields read environment variables during initialization, we need to set the environment variables before the class loads. This is why we can't simply set environment variables inside test methods - they would be set too late after the static fields are already initialized.

Each settings file configures environment variables like `IS_SCALE_OUT`, `IS_HOSTNAME`, and `IS_PORT` before any code runs, ensuring the static fields are initialized with the correct values for each test mode.

The test classes follow this structure:
- `LeaderElectionServiceTestBase.cs` - Base class containing common setup code
- `LeaderElectionServiceTests.cs` - Mode-agnostic tests that run in any mode
- `LeaderElectionServiceMultiInstanceTests.cs` - Multi-instance mode specific tests 
- `LeaderElectionServiceSingleInstanceTests.cs` - Single-instance mode specific tests

## Support and Feedback

### Getting Help

If you encounter issues or have questions about ImpowerSurvey:

- **Documentation**: Refer to the docs folder for detailed information on system architecture, SHIELD privacy principles, and implementation details
- **Email Support**: Contact support@impower.ai with technical issues
- **Bug Reports & Feature Requests**: Submit through our [GitHub Issues page](https://github.com/impower-ai/ImpowerSurvey/issues)

### Sample Data

First-time users can use the "Create Examples" button in the Survey Management page to generate sample surveys with demonstration questions. This allows you to explore the system's capabilities without creating content from scratch.

## Code Style

The project follows standard C# coding conventions with some specifics:
- Tabs for indentation
- PascalCase for classes, methods, and properties
- camelCase for private fields
- ALL_CAPS for constants

## License

```
   Copyright 2025 Impower.AI

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
```

### Why Apache 2.0?

The Apache 2.0 license was chosen specifically to:

- Allow free use, modification, and distribution of our code
- Provide patent protection for innovations like the SHIELD principle
- Prevent "submarine patenting" of techniques disclosed in this codebase
- Ensure attribution while enabling widespread adoption

This license supports our goal of showcasing code quality while protecting both users and contributors.

## Disclaimer

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

This software is not intended for use in any situation where failure could lead to injury, loss of life, or significant property damage. Users implementing this software in production environments do so at their own risk and are responsible for ensuring appropriate security, reliability, and compliance with all applicable laws and regulations.