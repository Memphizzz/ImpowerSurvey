# Architecture Analysis

## Overview

This document provides an analysis of the architectural design choices in the ImpowerSurvey application, highlighting design patterns.

## Design Characteristics

The codebase is structured with:

- Separation of concerns between components
- Comprehensive logging throughout
- SHIELD privacy principles implementation
- Extensive test coverage
- Modular organization of functionality

## Model-View-Controller Pattern Implementation

The codebase follows a modern adaptation of the MVC pattern:

### Model
- **Model Classes**: In `/Components/Model/` folder (User.cs, Survey.cs, Question.cs, etc.)
- **Data Access**: SurveyDbContext that manages database interactions
- **Business Rules**: Validation logic, data relationships, and entity behavior

The model layer cleanly represents data and business rules without UI concerns.

### View
- **Razor Components**: All `.razor` files in `/Components/Controls/` and `/Components/Pages/`
- **Layouts**: `/Components/Layout/` folder with MainLayout, MobileLayout
- **UI-specific Extensions**: Methods that format data for display

Views are separated from business logic, focusing only on presentation.

### Controller
- **Services Layer**: All classes in `/Services/` folder
- **Code-behind Files**: The `.razor.cs` partial classes that handle component logic
- **State Management**: CustomAuthStateProvider that manages authentication state

The controller layer properly separates UI concerns from business logic.

## Advanced Architectural Patterns

Beyond basic MVC, the codebase implements several additional patterns:

1. **Dependency Injection**
   - Services registered in Program.cs
   - Constructor injection throughout the codebase

2. **Repository Pattern** (variation)
   - DbContext and services that encapsulate data access

3. **Service Layer Pattern**
   - Well-defined services with clear responsibilities

4. **Clean Architecture Principles**
   - Dependencies point inward (UI depends on services, services depend on models)
   - Separation of infrastructure concerns from business logic

5. **Leader Election Pattern**
   - Coordinates work across multiple application instances
   - Uses database as coordination mechanism with heartbeat system
   - Provides automatic failover with configurable timeouts
   - Ensures critical operations are performed by only one instance

6. **Circuit Breaker Pattern**
   - Startup verification for inter-instance communication
   - Prevents startup if leader communication fails
   - Ensures data consistency and prevents response loss

## Multi-Instance Architecture

The application supports horizontal scaling through a sophisticated multi-instance architecture:

### Leader-Follower Model
- One instance serves as the leader for critical operations
- Leader is elected through database coordination
- Followers forward responses to leader via internal API
- Leader runs scheduled tasks and processes the Delayed Submission Service queue

### Instance Communication System
- HTTP-based communication between instances
- Secret-based authentication with `IS_INSTANCE_SECRET`
- Support for three primary operations:
  - NoOp: Communication verification
  - TransferResponses: Send survey responses to leader
  - FlushSurvey: Request leader to process pending responses

### Scale-Out Modes
The application operates in two distinct modes:

#### Single-Instance Mode (`IS_SCALE_OUT=false`)
- Simplified operation without leader election overhead
- Reduced database queries and operations
- Current instance automatically becomes leader
- Ideal for development or vertically scaling scenarios

#### Multi-Instance Mode (`IS_SCALE_OUT=true`)
- Full leader election implementation
- Database-coordinated leader identification
- Heartbeat-based leader monitoring
- Automatic failover when leader becomes unavailable
- Inter-instance communication for response handling

### Instance Identity
- Each instance has a unique identifier: `[HOSTNAME]:[PORT]`
- Configurable through environment variables (`IS_HOSTNAME`, `IS_PORT`)
- Defaults to standard environment-provided values if not explicitly set

### Deployment Considerations
- Instances must share the same database
- Inter-instance communication requires HTTP connectivity
- All instances must use the same `IS_INSTANCE_SECRET` for authentication
- Startup validation ensures instances can communicate with the leader
- Compatible with platforms supporting container-to-container communication

## Services vs. Traditional Controllers

The architecture uses services rather than traditional controllers, which provides several advantages:

1. **Cleaner Business Logic Isolation**
   - Services contain pure business logic without HTTP/UI dependencies
   - Example: `SurveyService` handles survey operations without knowing if it's called from a web page, API, or background job

2. **More Reusable Components**
   - Services can be used by multiple UI components
   - Example: `UserService` can be used by login pages, user management, and background processes

3. **State Management**
   - Architecture uses proper state management with AuthStateProvider
   - This allows for reactive UI updates when state changes

4. **Two-Level Controller Pattern**
   - Razor component code-behind (.razor.cs) - handles UI events and interactions
   - Services - handles business operations and data access
   - Creates a cleaner separation between "UI logic" and "business logic"

## Pragmatic Design Approach

The architecture takes a pragmatic approach that avoids common pitfalls:

1. **No Excessive Abstraction**
   - Code is direct and to-the-point with clear service methods

2. **No Complex Binding Frameworks**
   - Blazor components handle state simply with C# properties and methods

3. **No Command Pattern Overuse**
   - Code uses direct method calls for clarity

4. **No Presentation Models Duplication**
   - Keeps model structure clean and focused

The result is a modern, practical approach that:
- Uses services for business logic
- Keeps UI components focused on presentation
- Maintains a clear flow of data and responsibility
- Avoids unnecessary abstractions and boilerplate

The architecture follows a pattern where outer layers (UI) depend on inner layers (services → models), achieving separation of concerns without unnecessary complexity.