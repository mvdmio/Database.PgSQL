namespace mvdmio.Database.PgSQL.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class ColumnAttribute : Attribute
{
   public string Name { get; }
   
   public ColumnAttribute(string name)
   {
      Name = name;
   }
}