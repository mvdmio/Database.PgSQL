using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class PostDeployPartTests
{
   [Fact]
   public void Exists_WithImplicitImplementation_ReturnsTrue()
   {
      PostDeployPart.Exists(new ImplicitPostDeployMigration()).Should().BeTrue();
   }

   [Fact]
   public void Exists_WithExplicitImplementation_ReturnsTrue()
   {
      PostDeployPart.Exists(new ExplicitPostDeployMigration()).Should().BeTrue();
   }

   [Fact]
   public void Exists_WithImplementationInheritedFromBaseClass_ReturnsTrue()
   {
      PostDeployPart.Exists(new InheritingMigration()).Should().BeTrue();
   }

   [Fact]
   public void Exists_WithUpAsyncOnly_ReturnsFalse()
   {
      PostDeployPart.Exists(new UpOnlyMigration()).Should().BeFalse();
   }

   [Fact]
   public void Exists_WithPreDeployAsyncOnly_ReturnsFalse()
   {
      PostDeployPart.Exists(new PreDeployOnlyMigration()).Should().BeFalse();
   }

   private sealed class ImplicitPostDeployMigration : IDbMigration
   {
      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }

   private sealed class ExplicitPostDeployMigration : IDbMigration
   {
      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }

      Task IDbMigration.PostDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }

   private class BaseMigration : IDbMigration
   {
      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }

   private sealed class InheritingMigration : BaseMigration;

   private sealed class UpOnlyMigration : IDbMigration
   {
      public Task UpAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }

   private sealed class PreDeployOnlyMigration : IDbMigration
   {
      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }
}
