﻿//HintName: TestDbTable_Columns.g.cs
using System;
using mvdmio.Database.PgSQL;

namespace mvdmio.Database.PgSQL.Tests.Unit
{
    /// <auto-generated />
    public partial class TestDbTable : DbTable<TestDbTable>
    {
       private readonly DatabaseConnection _db;
    
       protected override string TableName => "test_table";
       protected override string Schema => "test_schema";
       protected override string[] Columns => new[] { "id", "first_name", "last_name", "email" };
       protected override string[] PrimaryKeyColumns => new[] { "id" };
       
       /// <summary>
       ///   Constructor.
       /// </summary>
       public TestDbTable(DatabaseConnection db)
         : base(db)
       {
         _db = db;
       }
    }
}
