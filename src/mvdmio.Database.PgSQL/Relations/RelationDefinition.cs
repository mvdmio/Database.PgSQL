using JetBrains.Annotations;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Relations;

/// <summary>
///    Declares a Relation between <typeparamref name="TDeclaring" /> and <typeparamref name="TTarget" />: the two
///    Table definitions it joins, named as its type arguments, and the Relation keys that resolve it.
/// </summary>
/// <remarks>
///    Purely declarative, in the same sense a Table definition is: a source generator reads what a derived class says
///    from source, and nothing ever instantiates one or calls its members. That is why a <see langword="private" />
///    nested class works — a syntax reader needs no access, where generated code calling into it would — and why the
///    class need not be nested inside the Table definition it belongs to at all: the type arguments say which two
///    tables are involved wherever the class lives. A Relation stays one-directional; declaring one never creates the
///    other.
/// </remarks>
/// <typeparam name="TDeclaring">The Table definition the Relation property is declared on.</typeparam>
/// <typeparam name="TTarget">The Table definition the Relation reaches.</typeparam>
[PublicAPI]
public abstract class RelationDefinition<TDeclaring, TTarget>
   where TDeclaring : class
   where TTarget : class
{
   /// <summary>
   ///    The Relation keys this Relation joins on — one pair per column, built with
   ///    <see cref="Key{TValue}(Expression{Func{TDeclaring,TValue}},Expression{Func{TTarget,TValue}})" /> or its
   ///    nullable-left overload. The order the pairs are listed in carries no meaning, because they are combined with
   ///    <c>&amp;&amp;</c>. A Relation with no pairs is a cross join, so there is no default: every derived class must
   ///    state at least one.
   /// </summary>
   public abstract IReadOnlyList<RelationKey> Keys { get; }

   /// <summary>
   ///    Builds one Relation key pairing a column on <typeparamref name="TDeclaring" /> against a column of the same
   ///    type on <typeparamref name="TTarget" />.
   /// </summary>
   /// <param name="declaringProperty">The property on <typeparamref name="TDeclaring" /> this key joins from.</param>
   /// <param name="targetProperty">The property on <typeparamref name="TTarget" /> this key joins to.</param>
   protected static RelationKey Key<TValue>(
      Expression<Func<TDeclaring, TValue>> declaringProperty,
      Expression<Func<TTarget, TValue>> targetProperty
   )
   {
      return new RelationKey();
   }

   /// <summary>
   ///    Builds one Relation key pairing a nullable column on <typeparamref name="TDeclaring" /> against a
   ///    non-nullable column of the same underlying type on <typeparamref name="TTarget" /> — the ordinary
   ///    outer-join shape, where a foreign key may hold null but the key it joins to never does.
   /// </summary>
   /// <param name="declaringProperty">The nullable property on <typeparamref name="TDeclaring" /> this key joins from.</param>
   /// <param name="targetProperty">The non-nullable property on <typeparamref name="TTarget" /> this key joins to.</param>
   protected static RelationKey Key<TValue>(
      Expression<Func<TDeclaring, TValue?>> declaringProperty,
      Expression<Func<TTarget, TValue>> targetProperty
   )
      where TValue : struct
   {
      return new RelationKey();
   }
}

/// <summary>
///    One pair of columns a <see cref="RelationDefinition{TDeclaring,TTarget}" /> states as equal. Opaque: nothing
///    reads it at run time, because a source generator reads the syntax that built it rather than evaluating it.
/// </summary>
[PublicAPI]
public sealed class RelationKey
{
   internal RelationKey()
   {
   }
}
