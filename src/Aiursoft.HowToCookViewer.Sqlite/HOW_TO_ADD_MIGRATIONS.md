# How to Add Migrations for SQLite

This guide explains how to create database migrations for the SQLite provider in this project.

## Prerequisites

- Install the EF Core CLI tools globally:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

## Creating a Migration

**Important:** You do NOT need an existing SQLite database to create migrations. The `SqliteContextFactory` handles design-time operations.

1. Navigate to the SQLite project directory:
   ```bash
   cd ./src/Aiursoft.HowToCookViewer.Sqlite/
   ```

2. Create a new migration with a descriptive name:
   ```bash
   dotnet ef migrations add YourMigrationName \
     --context "SqliteContext" \
     -s ../Aiursoft.HowToCookViewer/Aiursoft.HowToCookViewer.csproj
   ```

3. Review the generated migration file in `./Migrations/` to ensure it matches your expectations.

## Example

```bash
cd ./src/Aiursoft.HowToCookViewer.Sqlite/
dotnet ef migrations add AddUserProfileTable \
  --context "SqliteContext" \
  -s ../Aiursoft.HowToCookViewer/Aiursoft.HowToCookViewer.csproj
```

## Important Notes

- **No Database Required:** Thanks to the design-time factory, you don't need an existing database file to create migrations.
- **Review Migrations:** Always review generated migrations carefully. EF Core might misinterpret schema changes (e.g., renaming a column as drop + add).
- **Startup Project:** The `-s` parameter specifies the startup project, which is necessary for EF Core to load configuration and dependencies.
- **Migrations are NOT Applied Automatically:** Creating a migration only generates the change script. The application will automatically apply pending migrations at startup.

## Removing the Last Migration

If you made a mistake, you can remove the most recent migration:

```bash
dotnet ef migrations remove --context "SqliteContext" -s ../Aiursoft.HowToCookViewer/Aiursoft.HowToCookViewer.csproj
```

## After Creating Migrations

After creating migrations for SQLite, remember to:
1. Create the corresponding migration for MySQL (see `../Aiursoft.HowToCookViewer.MySql/HOW_TO_ADD_MIGRATIONS.md`)
2. Create migrations for any other supported databases in your project
