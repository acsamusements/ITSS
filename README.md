# ITSS — .NET 10 Utility Library

A personal utility library targeting **.NET 10** that I use across various projects and solutions.  
Everything here is general-purpose, non-proprietary, and shared freely for anyone to use.

---

## 📦 NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Data.SqlClient` | 7.0.0 | SQL Server access |
| `Dapper` | 2.1.72 | IL-based object mapper for typed SQL queries |
| `System.Data.OleDb` | 10.0.7 | Microsoft Access (OleDb) access |
| `ClosedXML` | 0.105.0 | Excel read/write (cross-platform) |
| `CsvHelper` | 33.1.0 | CSV read/write |
| `Serilog` | 4.3.1 | Structured logging |
| `Serilog.Sinks.Console` | 6.1.1 | Serilog console sink |
| `Serilog.Sinks.File` | 7.0.0 | Serilog file sink |

---

## 🗂️ Contents

### Helpers

| Class | Platform | Description |
|---|---|---|
| [`SqlHelper`](#sqlhelper) | Any | Full sync + async SQL Server wrapper — typed queries via Dapper |
| [`AccessHelper`](#accesshelper) | Windows only | Full sync + async Microsoft Access (OleDb) wrapper |
| [`CsvHelper`](#csvhelper) | Any | CSV read/write for lists, `DataTable`, and files |
| [`ExcelHelper`](#excelhelper) | Any | Excel (.xlsx) read/write via ClosedXML |
| [`FileHelper`](#filehelper) | Any | File I/O with retry-on-lock, JSON/XML convenience methods |
| [`HttpHelper`](#httphelper) | Any | Configurable static `HttpClient` wrapper |
| [`RetryHelper`](#retryhelper) | Any | Lightweight retry with back-off for sync and async operations |
| [`SecurityHelper`](#securityhelper) | Any | Hashing, AES encryption, token/password generation |
| [`ProcessHelper`](#processhelper) | Any | Launch external processes, capture stdout/stderr |
| [`RegistryHelper`](#registryhelper) | Windows only | Windows Registry read/write/delete |
| [`ConsoleHelper`](#consolehelper) | Any | Colorized output, tables, progress bar, spinner, prompts |

### Extensions

| Class | Description |
|---|---|
| [`StringExtensions`](#stringextensions) | Truncate, Left/Right, JSON/XML serialize, padding, title case |
| [`DateTimeExtensions`](#datetimeextensions) | Weekend/weekday, start/end of period, age, relative strings |
| [`ConversionExtensions`](#conversionextensions) | `object?` → int / decimal / double / bool / long / short |
| [`MathExtensions`](#mathextensions) | Safe division, clamp, round, IsNumeric/IsDecimal |
| [`EnumExtensions`](#enumextensions) | `[Description]` attribute, ToEnum, GetValues, dropdown pairs |
| [`CollectionExtensions`](#collectionextensions) | IsNullOrEmpty, ForEach, Batch, WhereNotNull, AddIfNotExists |
| [`DataExtensions`](#dataextensions) | `DataRow.To<T>()`, `DataTable.ToList<T>()` |
| [`ValidationExtensions`](#validationextensions) | Email, URL, phone, ZIP, password strength, IP address |
| [`ReflectionExtensions`](#reflectionextensions) | Property copy/map, get/set by name, type inspection |
| [`SerilogExtensions`](#serilogextensions) | Extended Debug/Error/Warning with compile-time caller context |

### Models

| Class | Description |
|---|---|
| [`RecordSet`](#recordset) | VB6-style ADO `Recordset` emulation over a SQL Server `DataTable` — BOF/EOF, MoveNext, AddNew, Edit, Update, Delete, UpdateBatch |

### Other

| Class | Description |
|---|---|
| [`Result<T>` / `Result`](#resultt) | Lightweight discriminated union for success/failure without exceptions |

---

## 🚀 Quick Start

### SqlHelper

Initialize once at startup, then call anywhere.  
`GetList<T>` / `ExecuteScalar<T>` / `ExecuteNonQuery` use **Dapper** internally for fast IL-based
object mapping. `GetDataTable` retains ADO.NET for callers that need a raw `DataTable`.

```csharp
SqlHelper.Initialize(o =>
{
    o.DefaultConnectionString = "Server=.;Database=MyDb;Trusted_Connection=True;";
    o.Logger = myLogger;
    o.ThrowOnError = false; // swallow errors and return safe defaults
});

// Typed list — Dapper IL mapper, no DataTable allocation
var users = await SqlHelper.GetListAsync<User>("SELECT * FROM Users WHERE Active = @Active",
    parameters: [new SqlParameter("@Active", true)]);

// Scalar — Dapper ExecuteScalarAsync<T>
var count = await SqlHelper.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders");

// Non-query with dictionary params — Dapper ExecuteAsync
int rows = await SqlHelper.ExecuteNonQueryAsync(
    "UPDATE Users SET LastLogin = @Date WHERE Id = @Id",
    parameters: new Dictionary<string, object?> { ["Date"] = DateTime.Now, ["Id"] = 42 });

// Stored procedure — DataTable (ADO.NET)
var dt = await SqlHelper.GetDataTableAsync("usp_GetReport",
    commandType: CommandType.StoredProcedure);

// Build SqlParameter[] from any object's public properties
var p = SqlHelper.GetSqlParameters(new { Active = true, DeptId = 5 });
var list = SqlHelper.GetList<User>("SELECT * FROM Users WHERE Active = @Active AND DeptId = @DeptId", p);
```

---

### AccessHelper

Windows-only — requires the Microsoft ACE OleDb provider.  
Uses positional `?` parameters (OleDb does not support named parameters).

```csharp
// Guard for non-Windows environments
if (OperatingSystem.IsWindows())
{
    AccessHelper.Initialize(o =>
    {
        o.DefaultConnectionString = @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\data\mydb.accdb;";
    });

    // Positional ? placeholders — order must match the parameter list
    var orders = await AccessHelper.GetListAsync<Order>(
        "SELECT * FROM Orders WHERE Status = ? AND Year = ?",
        parameters: ["Active", 2024]);
}
```

---

### CsvHelper

```csharp
// Write list → file
CsvHelper.WriteToFile("output.csv", myList);

// Read file → list
var records = CsvHelper.ReadFromFile<MyRecord>("data.csv");

// DataTable → CSV string
string csv = CsvHelper.DataTableToCsvString(myDataTable);

// CSV string → DataTable
DataTable dt = CsvHelper.StringToDataTable(csvContent);
```

---

### ExcelHelper

No Office installation required — uses ClosedXML.

```csharp
// DataTable → xlsx
ExcelHelper.DataTableToFile("report.xlsx", myDataTable);

// List<T> → xlsx
ExcelHelper.ListToFile("users.xlsx", userList, sheetName: "Users");

// xlsx → DataTable
DataTable dt = ExcelHelper.FileToDataTable("report.xlsx");

// xlsx → List<T>
var users = ExcelHelper.FileToList<User>("users.xlsx");

// DataTable → byte[] (useful for HTTP responses)
byte[] bytes = ExcelHelper.DataTableToBytes(myDataTable);

// Multiple sheets
ExcelHelper.DataSetToFile("multi.xlsx", myDataSet);
```

---

### FileHelper

```csharp
// Read / write with retry on file lock
string text = FileHelper.ReadAllText("config.txt");
FileHelper.WriteAllText("output.txt", content);

// JSON
var config = FileHelper.ReadJson<AppConfig>("appsettings.json");
await FileHelper.WriteJsonAsync("appsettings.json", config);

// XML
var data = FileHelper.ReadXml<MyModel>("data.xml");
FileHelper.WriteXml("data.xml", myModel);

// Safe operations
FileHelper.SafeDelete("old.tmp");
FileHelper.SafeMove("source.txt", "archive/source.txt");

// Streaming lines (no full load into memory)
await foreach (var line in FileHelper.ReadLinesAsync("big.log"))
    Process(line);
```

---

### HttpHelper

```csharp
HttpHelper.Initialize(o =>
{
    o.BaseUrl = "https://api.example.com";
    o.Timeout = TimeSpan.FromSeconds(15);
    o.DefaultHeaders["Authorization"] = "Bearer my-token";
    o.ThrowOnError = false;
});

// GET
var user = await HttpHelper.GetAsync<User>("/users/42");

// POST
var created = await HttpHelper.PostAsync<CreateUserRequest, User>("/users", new CreateUserRequest(...));

// PUT / PATCH / DELETE
var updated = await HttpHelper.PutAsync<UpdateRequest, User>("/users/42", updateRequest);
bool deleted = await HttpHelper.DeleteAsync("/users/42");

// Download file
await HttpHelper.DownloadFileAsync("https://example.com/file.pdf", @"C:\downloads\file.pdf");
```

---

### RetryHelper

```csharp
// Sync with exponential back-off, only retry on SqlException
var result = RetryHelper.Retry(
    () => SqlHelper.ExecuteScalar<int>("SELECT 1"),
    maxAttempts: 4,
    delayMs: 250,
    exponentialBackoff: true,
    retryOn: ex => ex is SqlException);

// Async
await RetryHelper.RetryAsync(
    () => HttpHelper.GetAsync<User>("/users/1"),
    maxAttempts: 3,
    delayMs: 500);
```

---

### SecurityHelper

```csharp
// Hashing
string sha256 = SecurityHelper.HashSha256("my secret");
string md5    = SecurityHelper.HashMd5("checksum input");   // non-security use only
string hmac   = SecurityHelper.HmacSha256("payload", "key");

// File hash
string hash = await SecurityHelper.ComputeFileHashSha256Async("installer.exe");

// AES-256 encryption
string cipher = SecurityHelper.EncryptAes("sensitive data", "my-key");
string? plain = SecurityHelper.DecryptAes(cipher, "my-key");

// Token / password generation
string token    = SecurityHelper.GenerateRandomToken();    // 256-bit URL-safe token
string pin      = SecurityHelper.GeneratePin(6);
string password = SecurityHelper.GeneratePassword(16);

// Constant-time comparison (prevents timing attacks)
bool match = SecurityHelper.SecureEquals(inputToken, storedToken);
```

---

### ProcessHelper

```csharp
// Run executable
var result = ProcessHelper.Run("git", "status", workingDirectory: @"C:\repos\myproject");
if (result.Success)
    Console.WriteLine(result.StandardOutput);

// Async with cancellation
var result = await ProcessHelper.RunAsync("dotnet", "build", throwOnError: true, cancellationToken: ct);

// Shell command
var result = ProcessHelper.RunShell("ping 8.8.8.8 -n 1");

// Open with default app (browser, Explorer, etc.)
ProcessHelper.OpenWithDefault("https://github.com");
ProcessHelper.OpenWithDefault(@"C:\reports\report.xlsx");
```

---

### RegistryHelper

Windows only — annotated with `[SupportedOSPlatform("windows")]`.

```csharp
if (OperatingSystem.IsWindows())
{
    // Read
    string? path = RegistryHelper.GetString(RegistryHive.LocalMachine,
        @"SOFTWARE\MyApp", "InstallPath");

    // Write
    RegistryHelper.SetValue(RegistryHive.CurrentUser,
        @"SOFTWARE\MyApp\Settings", "Theme", "Dark");

    // Check existence
    bool exists = RegistryHelper.KeyExists(RegistryHive.LocalMachine, @"SOFTWARE\MyApp");

    // Delete
    RegistryHelper.DeleteValue(RegistryHive.CurrentUser, @"SOFTWARE\MyApp\Settings", "Theme");
}
```

---

### ConsoleHelper

```csharp
// Colorized output
ConsoleHelper.WriteSuccess("Build completed.");
ConsoleHelper.WriteError("Connection failed.");
ConsoleHelper.WriteWarning("Retrying...");
ConsoleHelper.WriteInfo("Starting import...");
ConsoleHelper.WriteHeader("Report Summary");

// Render a DataTable as an ASCII table
ConsoleHelper.WriteTable(myDataTable);

// Progress bar (call in a loop)
for (int i = 0; i <= 100; i++)
    ConsoleHelper.WriteProgress(i, 100, label: $"Processing record {i}");

// Spinner while blocking work runs
ConsoleHelper.WithSpinner(() => DoHeavyWork(), "Loading data");

// Async spinner
await ConsoleHelper.WithSpinnerAsync(() => LoadDataAsync(), "Fetching records");

// Prompts
string name     = ConsoleHelper.Prompt("Enter your name", defaultValue: "Anonymous");
bool   confirm  = ConsoleHelper.PromptYesNo("Continue?");
string password = ConsoleHelper.PromptPassword();
ConsoleHelper.PauseForKey();
```

---

### StringExtensions

```csharp
"Hello World".Truncate(5);                  // "Hello"
"Hello World".Left(5);                      // "Hello"
"Hello World".Right(5);                     // "World"
"hello world".ToTitleCase();                // "Hello World"
"  hello  ".RemoveWhitespace();             // "hello"
"abc".Repeat(3);                            // "abcabcabc"
myObject.SerializeObject();                 // JSON string
"{ }".DeserializeObject<MyModel>();
"<root/>".DeserializeXml<MyModel>();
"hello".IsNullOrEmpty();
"hello".IsNullOrWhiteSpace();
"hello".Contains(new[] { "ell", "xyz" });   // true (case-insensitive)
```

---

### DateTimeExtensions

```csharp
DateTime.Today.IsWeekend();
DateTime.Today.IsWeekday();
DateTime.Now.StartOfDay();
DateTime.Now.EndOfDay();
DateTime.Now.StartOfMonth();
DateTime.Now.EndOfMonth();
DateTime.Now.StartOfWeek(DayOfWeek.Monday);
new DateTime(1990, 6, 15).ToAge();          // age in whole years
DateTime.Now.ToRelativeString();            // "3 hours ago"
someDate.IsBetween(start, end);
```

---

### ConversionExtensions

```csharp
object? raw = reader["Amount"];
double  d   = raw.ToDouble();
decimal dec = raw.ToDecimal();
int     i   = raw.ToInt32();
bool    b   = raw.ToBoolean();   // handles "yes"/"no"/"1"/"0"/"true"/"false"
```

---

### MathExtensions

```csharp
(10m).ZDiv(0m);                  // 0  — safe division, no DivideByZeroException
"abc123".MakeNumeric();          // "123"
"123".IsNumeric();               // true
"12.3".IsDecimal();              // true
(3.14159m).RoundTo(2);           // 3.14
(150).Clamp(0, 100);             // 100
```

---

### EnumExtensions

```csharp
MyEnum.Active.GetDescription();                      // reads [Description] attribute
"active".ToEnum<MyEnum>();                           // MyEnum.Active (case-insensitive)
EnumExtensions.GetValues<MyEnum>();
EnumExtensions.GetValuesWithDescriptions<MyEnum>();  // IEnumerable<(T Value, string Description)>
MyEnum.Active.IsDefined();
```

---

### CollectionExtensions

```csharp
list.IsNullOrEmpty();
list.ForEach(item => Process(item));
list.WhereNotNull();
list.AddIfNotExists(newItem);
list.Batch(50);                  // IEnumerable<IEnumerable<T>> of size 50
list.IndexOf(x => x.Id == 42);
```

---

### DataExtensions

```csharp
// Map a DataRow to a typed object
MyModel obj = row.To<MyModel>();

// Map a DataTable to a typed list
List<MyModel> list = table.ToList<MyModel>();

// Works with single-column scalar projections too
List<int> ids = table.ToList<int>();
```

---

### ValidationExtensions

```csharp
"user@example.com".IsValidEmail();
"https://example.com".IsValidUrl();
"(555) 123-4567".IsValidPhone();
"90210".IsValidZip();
"90210-1234".IsValidZip();
"abc123".IsAlphanumeric();
"Hello1!".IsStrongPassword();
"192.168.1.1".IsValidIpAddress();
"P@ssw0rd!".IsLengthBetween(8, 64);
```

---

### ReflectionExtensions

```csharp
// Copy matching properties between objects
source.CopyPropertiesTo(target);

// Map to a new instance
var dto = entity.MapTo<UserDto>();

// Get/set by name (case-insensitive)
object? val = obj.GetPropertyValue("Name");
int age     = obj.GetPropertyValue<int>("Age");
obj.SetPropertyValue("Name", "Alice");

// Type inspection
typeof(MyClass).HasProperty("Id");
typeof(MyClass).Implements<IDisposable>();
typeof(int?).IsNullable();
typeof(decimal).IsNumeric();

// Dump all properties as dictionary
var dict = myObject.ToDictionary();

// Attribute helpers
var attr = prop.GetAttribute<RequiredAttribute>();
bool has  = method.HasAttribute<ObsoleteAttribute>();
```

---

### SerilogExtensions

```csharp
// Includes class name and method name in the log message automatically
// using CallerMemberName / CallerFilePath — zero runtime overhead.
_log.ExtendedDebug("Starting import");
_log.ExtendedError("Failed to connect", ex);
_log.ExtendedWarning("Retry attempt 2");
_log.ExtendedInfo("Loaded 500 rows");
```

---

### Result\<T\>

A lightweight discriminated union — return success or failure without throwing exceptions.

```csharp
// Returning results
Result<User> GetUser(int id)
{
    if (id <= 0) return Result<User>.Fail("Invalid id");
    var user = db.Find(id);
    return user is null ? Result<User>.Fail("Not found") : Result<User>.Ok(user);
}

// Consuming results
var result = GetUser(42);

if (result.IsSuccess)
    Console.WriteLine(result.Value.Name);
else
    Console.WriteLine(result.Error);

// Fluent chaining
result
    .OnSuccess(u => Console.WriteLine($"Welcome, {u.Name}"))
    .OnFailure(err => Console.WriteLine($"Error: {err}"));

// Map / Bind
Result<string> nameResult = result.Map(u => u.Name);

// Deconstruction
var (ok, value, error) = result;

// Wrap an existing call
var r = Result<int>.Try(() => int.Parse(input));
var r = await Result<User>.TryAsync(() => GetUserAsync(id));

// Non-generic (void operations)
Result op = Result.Try(() => File.Delete("old.tmp"));
if (op.IsFailure) log.Error(op.Error);
```

---

### RecordSet

A VB6-style ADO `Recordset` emulation backed by a `DataTable`, designed to ease migration of
legacy VB6 codebases to .NET. Supports the classic cursor navigation model (BOF/EOF, MoveNext,
MoveLast, AbsolutePosition), in-place editing (Edit/Update/CancelUpdate), batch persistence
(UpdateBatch), and direct SQL execution via `SqlConnection`.

```csharp
// ── Connection setup (once per instance or shared) ────────────────────────
var rs = new RecordSet();
rs.SetDefaultConnection("Server=.;Database=MyDb;Trusted_Connection=True;");

// Alternatively, share an existing open connection
rs.SetSharedConnection(existingConnection);

// ── Open / navigate ───────────────────────────────────────────────────────
rs.Open("SELECT * FROM Orders WHERE Status = @Status",
    new Dictionary<string, object> { ["@Status"] = "Open" });

while (!rs.EOF)
{
    Console.WriteLine(rs["OrderId"]);   // field access by name
    Console.WriteLine(rs[0]);           // field access by index
    rs.MoveNext();
}

// ── Edit a record ─────────────────────────────────────────────────────────
rs.MoveFirst();
rs.Edit();
rs["Status"] = "Closed";
rs.Update();

// ── Add a new record ──────────────────────────────────────────────────────
rs.AddNew();
rs["OrderId"]   = 9999;
rs["Status"]    = "Open";
rs["CreatedAt"] = DateTime.Now;
rs.Update();

// ── Delete current record ─────────────────────────────────────────────────
rs.MoveFirst();
rs.Delete();
rs.MoveNext();   // move off the deleted row before accessing the next

// ── Persist all pending inserts / edits / deletes to the database ─────────
rs.UpdateBatch();

// ── Search ────────────────────────────────────────────────────────────────
if (rs.Find("Status = 'Open'"))
    Console.WriteLine($"Found at position {rs.AbsolutePosition}");

// ── Requery (re-execute the original SQL) ────────────────────────────────
rs.Requery();

// ── Clone (copies schema + data, mirrors VB6 rs.Clone) ────────────────────
RecordSet rs2 = rs.Clone();

// ── Execute helpers (fire-and-forget SQL on the same connection) ──────────
rs.Execute("DELETE FROM Logs WHERE CreatedAt < @Cutoff",
    new Dictionary<string, object> { ["@Cutoff"] = DateTime.Now.AddDays(-30) });

int orderCount = rs.ExecuteScalar<int>("SELECT COUNT(*) FROM Orders");

// ── Using with a DataTable you already have ───────────────────────────────
using var rs3 = new RecordSet(existingDataTable);
while (!rs3.EOF)
{
    Process(rs3.CurrentRow!);
    rs3.MoveNext();
}

// ── Always dispose when done ──────────────────────────────────────────────
rs.Dispose();
// or: using var rs = new RecordSet();
```

> **Note:** `RecordSet` uses `SqlDataAdapter` + `SqlCommandBuilder` for `UpdateBatch`, which
> requires the query to target a single table with a primary key. For complex joins, handle
> persistence manually with `SqlHelper` or `rs.Execute()`.

---

## ⚠️ Platform Notes

| Component | Requirement |
|---|---|
| `AccessHelper` | Windows only (`[SupportedOSPlatform("windows")]`) — requires Microsoft ACE OleDb provider |
| `RegistryHelper` | Windows only (`[SupportedOSPlatform("windows")]`) |
| All others | Cross-platform (.NET 10+) |

Non-Windows callers should guard Access/Registry usage with `OperatingSystem.IsWindows()` — the Roslyn CA1416 analyzer will warn at compile time on unguarded call sites.

---

## 📋 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- For `AccessHelper`: [Microsoft Access Database Engine 2016 Redistributable](https://www.microsoft.com/en-us/download/details.aspx?id=54920) (Windows only)

---

## 📄 License

MIT — free to use, modify, and distribute.
