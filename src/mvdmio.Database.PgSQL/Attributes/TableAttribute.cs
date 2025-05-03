namespace mvdmio.Database.PgSQL.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class TableAttribute : Attribute
{
   public string Name { get; }
   public string Schema { get; }

   public TableAttribute(string name, string schema = "public")
   {
      Name = name;
      Schema = schema;
   }
}