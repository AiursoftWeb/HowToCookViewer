# Aiursoft Entity Framework Core Entity Modeling Specification

## Core Philosophy: Dictator Mode

1.  **DbSet Dictatorship**: Data creation, update, and deletion (CUD) must be performed through `DbContext.DbSet<T>`.
2.  **Explicit Loading**: Implicit behaviors (such as lazy loading) are prohibited; data must be explicitly `Include`d.
3.  **Compile-time Contracts**: Leverage C# type system (Type System) to expose potential errors at compile time, rather than deferring them to runtime.
4.  **Table Visibility**: All entity classes must clearly express their data columns and relationships, avoiding implicit conventions. All entity classes must appear in `DbContext` as `public DbSet<Repo> Repos => Set<Repo>();`, and table names must be the plural form of the entity class name (e.g., `User` corresponds to `Users` table).

-----

## 1. Primary Key Specification

**1.1 Type Restrictions**
The primary key of all entity classes must be of type `int` or `Guid`.

  * **Exception**: Entity classes that directly inherit from `Microsoft.AspNetCore.Identity.IdentityUser` are exempt (default uses `string`, allowed to remain unchanged).

**1.2 Mandatory Attribute**
The primary key of all entity classes must be decorated with the `[Key]` attribute.

**1.3 Immutability**
The primary key of all entity classes must be `{ get; init; }` (immutable after creation).

> **Example:**
>
> ```csharp
> [Key]
> public Guid Id { get; init; }
> ```

-----

## 2. Foreign Keys & Navigation Specification

**2.1 Pair Definition**
All foreign key relationships must be explicitly defined with two properties: the **foreign key ID** and the **navigation reference**.

**2.2 Foreign Key ID Requirement**

  * **Required relationships**: The foreign key ID must be `Guid`, `string`, or `int`, and the property must be `required`. In this case, the foreign key is non-nullable.
  * **Optional relationships**: The foreign key ID must be `Guid?`, `string?`, or `int?`, and the property must be `required`. In this case, the foreign key is nullable. During initialization, it **must be explicitly assigned a value of `null`**.

**2.3 Nullability of Navigation References**

All **navigation reference properties** must be declared as nullable types (e.g., `User?`), even if they are designed to never be null. This is because it's impossible to have a reasonable value when instantiating the object, so the property must be nullable. In practice, if this navigation property is not nullable, it will never return null.

  * *Reason*: When using `new Entity()`, we typically only assign the ID and not the object. Declaring it as nullable satisfies the C# compiler's initialization checks and avoids false warnings.

**2.4 Navigation Properties Must Be NotNull**

All **navigation reference properties** that are guaranteed to never be null must be annotated with `[NotNull]`; otherwise, `[NotNull]` must not be used.

  * *Purpose*: To prevent null reference warnings from the compiler when using navigation properties.
  * *Note*: Even when `[NotNull]` is applied, the property type must still be a nullable type (e.g., `User?`).

**2.5 Serialization Ignoring**

All **navigation reference properties** must be annotated with `[Newtonsoft.Json.JsonIgnore]` (to prevent infinite loops).

**2.6 No Virtual (No Lazy Loading)**

All navigation properties **must not** use the `virtual` keyword.

  * *Consequence*: Lazy Loading Proxy is completely disabled.
  * *Requirement*: When querying related data, `.Include()` and `.ThenInclude()` must be explicitly called.

> **Example:**
>
> ```csharp
> // Foreign key ID (required)
> public required Guid UserId { get; set; }
>
> // Navigation reference (must be nullable, must be NotNull,禁止 virtual)
> [JsonIgnore]
> [ForeignKey(nameof(UserId))]
> [NotNull] // Non-nullable, add [NotNull]
> public User? User { get; set; } // Non-nullable, also add question mark.
> ```

-----

## 3. Collections & Dictatorship

**3.1 Type Restrictions**
All reverse navigation collections must be declared as `IEnumerable<T>` type.

  * *Purpose*: Strip `.Add()` and `.Remove()` methods from collections at compile time.

**3.2 Initialization**
All collection properties must be initialized to `new List<T>()` to prevent null reference exceptions.

**3.3 Explicit Association**
All collection properties must be decorated with `[InverseProperty]`.

**3.4 Dictatorship Modification Principle**
Prohibit casting `IEnumerable` to `List` for data manipulation.

  * **Add child item**: Must create a new object and `_dbContext.Add(newItem)`.
  * **Remove child item**: Must query the object and `_dbContext.Remove(item)`.

> **Example:**
>
> ```csharp
> [InverseProperty(nameof(ExamPaperSubmission.User))]
> public IEnumerable<ExamPaperSubmission> Submissions { get; init; } = new List<ExamPaperSubmission>();
> ```

-----

## 4. Data Columns

**4.1 Length Constraints**
All `string` or `byte[]` columns must be annotated with `[MaxLength]`.

**4.2 Null Semantics**

  * **Non-nullable Column**: Use non-nullable types (e.g. `string`), and never use `[NotNull]` on nullable types.
  * **Nullable Column**: Use nullable types (e.g. `string?`), and must specify in XML comments what **business state null represents**.

**4.3 Immutable Properties**
All business properties that are not editable (e.g. `CreationTime`) must be `{ get; init; }`.

-----

## 5. Complete Standard HowToCookViewer (V3.0)

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis; // 尽量少用，除非用于辅助方法
using Newtonsoft.Json;

namespace Aiursoft.Exam.Entities;

public class HowToCookViewerEntity
{
    // [规则 1.1, 1.2, 1.3] 主键：Guid/int, Key, init
    [Key]
    public Guid Id { get; init; }

    // [规则 4.1, 4.2] 必填列：required string, MaxLength, 禁止 [NotNull]
    [MaxLength(100)]
    public required string Name { get; set; }

    // [规则 4.2, 4.3] 可空列：string?, 注释说明含义, init (如果是创建时不变量)
    /// <summary>
    /// 描述信息。
    /// 若为空，表示用户在创建时未填写备注。
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; init; }

    // [规则 4.3] 系统字段
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    // ================= 关联关系 =================

    // [规则 2.2] 外键ID：required (即使是 Guid? 也要 required 以强制显式赋值)
    public required Guid ParentId { get; set; }

    // [规则 2.3, 2.4, 2.5, 2.6] 
    // 导航引用：Type?, JsonIgnore, ForeignKey, NotNull
    // 严禁 virtual (禁用延迟加载)
    [JsonIgnore]
    [ForeignKey(nameof(ParentId))]
    [NotNull]
    public ParentEntity? Parent { get; set; }

    // ================= 关联关系，可空的 ================

    // [规则 2.2] 可选外键ID：required Guid? (实例化时，必须显式赋值 null)
    public required Guid? OptionalParentId { get; set; }

    // [规则 2.3, 2.4, 2.5, 2.6] 可选导航引用：Type?, JsonIgnore, ForeignKey
    // 严禁 NotNull (因为是可选的)，严禁 virtual
    [JsonIgnore]
    [ForeignKey(nameof(OptionalParentId))]
    public ParentEntity? OptionalParent { get; set; }

    // [规则 3.1, 3.2, 3.3] 
    // 集合：IEnumerable (独裁模式), InverseProperty, new List()
    // 严禁 virtual
    [InverseProperty(nameof(ChildEntity.Parent))]
    public IEnumerable<ChildEntity> Children { get; init; } = new List<ChildEntity>();
}
```
