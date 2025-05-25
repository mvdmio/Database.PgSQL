This is a C# NuGet package project. It provides the following to users:
- A Wrapper around Dapper that makes it easier to interact with PostgreSQL databases. This is the `DatabaseConnection` class.
- An abstraction for creating queries on Tables; These are the `DbTable` and `DbRecord` classes. The library provides a Source Generator that generates the `DbTable` and `DbRecord` classes for users.
- An abstraction for creating and executing database migrations; This is the `IDbMigration` interface.

# Code Standards

## Styling
- Use the C# coding conventions as defined by Microsoft.
- Use `dotnet format` to format your code before committing.

# Development flow
- Use the `main` branch for development.
- Run the build and tests before committing your changes.
- Use the `dotnet format` command to format your code before committing.
- Write unit tests or integration tests before making any changes. The tests should cover all the new code you want to write.
- All public methods must have XML documentation comments describing their behavior and parameters.