# PyToNet API 参考文档

## 概述

PyToNet是一个强大的.NET库，提供在.NET环境中执行Python代码的能力。本文档详细说明PyToNet库的所有API接口、参数、返回值和使用方法。

### 新特性 (v1.2.0)

- ✅ **完整的双向调用支持** - Python可以调用C#中已存在的全局对象
- ✅ **命名管道通信** - 使用真正的进程间通信，不是简化模拟
- ✅ **异步任务支持** - 所有核心功能都提供异步版本
- ✅ **复杂类型处理** - 支持JSON序列化和反序列化
- ✅ **类型安全扩展** - 增强的复杂类型转换功能
- ✅ **性能优化** - 异步执行避免阻塞主线程

## 核心类型

### PyToNetResult 结构体

表示PyToNet操作的通用结果类型。

```csharp
public struct PyToNetResult
{
    public bool Success { get; }
    public PyToNetErrorCode ErrorCode { get; }
    public string ErrorMessage { get; }
    public string Output { get; }
}
```

**属性说明：**
- `Success` - 操作是否成功完成
- `ErrorCode` - 错误码，仅在`Success`为`false`时有意义
- `ErrorMessage` - 英文错误描述信息
- `Output` - 执行输出内容

**静态方法：**
- `PyToNetResult Ok(string output = "")` - 创建成功结果
- `PyToNetResult Fail(PyToNetErrorCode errorCode, string errorMessage)` - 创建失败结果
- `PyToNetResult Fail(PyToNetErrorCode errorCode, Exception exception)` - 从异常创建失败结果

### PyToNetResult<T> 结构体

泛型结果类型，支持类型安全的返回值。

```csharp
public struct PyToNetResult<T>
{
    public bool Success { get; }
    public PyToNetErrorCode ErrorCode { get; }
    public string ErrorMessage { get; }
    public T Value { get; }
}
```

**属性说明：**
- `Value` - 类型安全的值，仅在`Success`为`true`时有意义

**静态方法：**
- `PyToNetResult<T> Ok(T value)` - 创建成功结果
- `PyToNetResult<T> Fail(PyToNetErrorCode errorCode, string errorMessage)` - 创建失败结果
- `PyToNetResult<T> Fail(PyToNetErrorCode errorCode, Exception exception)` - 从异常创建失败结果

### PyToNetErrorCode 枚举

定义所有可能的错误码。

```csharp
public enum PyToNetErrorCode
{
    // 成功
    Success = 0,
    
    // 环境初始化错误 (1000-1999)
    PythonPathNotFound = 1001,
    PythonExecutableNotFound = 1002,
    PythonProcessStartFailed = 1003,
    
    // Python执行错误 (2000-2999)
    PythonExecutionError = 2001,
    PythonFileNotFound = 2002,
    PythonSyntaxError = 2003,
    PythonRuntimeError = 2004,
    
    // 操作错误 (3000-3999)
    EnvironmentNotInitialized = 3001,
    InvalidOperation = 3002,
    
    // 对象模型错误 (4000-4999)
    ClassNotFound = 4001,
    InstanceCreationFailed = 4002,
    MethodCallFailed = 4003,
    PropertyAccessFailed = 4004,
    
    // 未知错误
    UnknownError = 9999
}
```

## PyToNetEngine 类

主要的API入口点，负责管理Python环境和执行操作。

### 构造函数

```csharp
public PyToNetEngine()
```

**说明：**
- 创建新的PyToNetEngine实例
- 需要调用`Init()`方法初始化Python环境后才能使用

### 核心方法

#### Init

```csharp
public PyToNetResult Init(string pythonPath)
```

**参数：**
- `pythonPath` - Python解释器的安装路径

**返回值：**
- `PyToNetResult` - 初始化结果

**错误码：**
- `PythonPathNotFound` - Python路径不存在
- `PythonExecutableNotFound` - Python可执行文件未找到
- `PythonProcessStartFailed` - Python进程启动失败

**示例：**
```csharp
var python = new PyToNetEngine();
var result = python.Init("C:\\Python311");
if (result.Success)
{
    Console.WriteLine("Python环境初始化成功");
}
else
{
    Console.WriteLine($"初始化失败: {result.ErrorMessage}");
}
```

#### Execute

```csharp
public PyToNetResult Execute(string pythonCode)
```

**参数：**
- `pythonCode` - 要执行的Python代码

**返回值：**
- `PyToNetResult` - 执行结果，包含输出内容

**错误码：**
- `EnvironmentNotInitialized` - 环境未初始化
- `PythonSyntaxError` - Python语法错误
- `PythonRuntimeError` - Python运行时错误

**示例：**
```csharp
var result = python.Execute("print('Hello World')");
if (result.Success)
{
    Console.WriteLine($"输出: {result.Output}"); // 输出: Hello World
}
```

#### ExecuteFile

```csharp
public PyToNetResult ExecuteFile(string filePath)
```

**参数：**
- `filePath` - Python脚本文件路径

**返回值：**
- `PyToNetResult` - 执行结果

**错误码：**
- `PythonFileNotFound` - Python文件未找到
- `PythonSyntaxError` - Python语法错误

**示例：**
```csharp
var result = python.ExecuteFile("script.py");
```

### 高级功能方法

#### Execute<T>

```csharp
public T Execute<T>(string pythonCode)
```

**参数：**
- `pythonCode` - 要执行的Python代码

**返回值：**
- `T` - 类型安全的结果值

**异常：**
- `InvalidOperationException` - 环境未初始化时抛出
- `InvalidCastException` - 类型转换失败时抛出

**说明：**
- 此方法会抛出异常而不是返回结果结构体
- 适用于确定操作会成功的场景
- 支持的类型转换：
  - **基本类型**: `int`, `double`, `float`, `bool`, `string`
  - **数值类型**: 自动进行数值转换
  - **字符串类型**: 直接返回Python输出的字符串
  - **复杂类型**: 需要Python代码返回JSON字符串，然后使用`System.Text.Json`反序列化

**示例：**
```csharp
// 基本类型转换
int result = python.Execute<int>("5 + 3"); // result = 8
double pi = python.Execute<double>("3.14159"); // pi = 3.14159
bool flag = python.Execute<bool>("True"); // flag = true
string text = python.Execute<string>("'Hello World'"); // text = "Hello World"

// 复杂类型处理（需要Python返回JSON）
var jsonCode = @"""
import json
person = {"name": "Alice", "age": 25, "active": True}
print(json.dumps(person))
""";

// 需要自定义反序列化逻辑
string jsonResult = python.Execute<string>(jsonCode);
// 然后使用 System.Text.Json.JsonSerializer.Deserialize<Person>(jsonResult)
```

**复杂类型处理说明：**

对于复杂类型，PyToNet不直接支持自动反序列化。您需要：

1. 在Python代码中返回JSON格式的字符串
2. 使用`Execute<string>()`获取JSON字符串
3. 使用`System.Text.Json`或其他JSON库进行反序列化

```csharp
// 自定义复杂类型处理
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public bool Active { get; set; }
}

// 执行Python代码获取JSON
string json = python.Execute<string>(@"""
import json
person = {"name": "Alice", "age": 25, "active": True}
print(json.dumps(person))
""");

// 手动反序列化
var person = System.Text.Json.JsonSerializer.Deserialize<Person>(json);
```

#### Execute (带类型参数)

```csharp
public object Execute(string pythonCode, Type returnType)
```

**参数：**
- `pythonCode` - 要执行的Python代码
- `returnType` - 期望的返回类型

**返回值：**
- `object` - 转换为指定类型的结果

**说明：**
- 动态类型转换，适用于运行时确定类型的情况

#### CallFunction

```csharp
public object CallFunction(string moduleName, string functionName, params object[] args)
```

**参数：**
- `moduleName` - Python模块名
- `functionName` - Python函数名
- `args` - 函数参数

**返回值：**
- `object` - 函数调用结果

**示例：**
```csharp
var result = python.CallFunction("math", "sqrt", 16.0); // result = 4.0
```

#### CallFunction<T>

```csharp
public T CallFunction<T>(string moduleName, string functionName, params object[] args)
```

**参数：**
- `moduleName` - Python模块名
- `functionName` - Python函数名
- `args` - 函数参数

**返回值：**
- `T` - 类型安全的函数调用结果

**示例：**
```csharp
double sqrt = python.CallFunction<double>("math", "sqrt", 16.0); // sqrt = 4.0
```

#### RegisterDotNetFunction

```csharp
public void RegisterDotNetFunction(string pythonFunctionName, Delegate dotNetFunction)
```

**参数：**
- `pythonFunctionName` - 在Python中使用的函数名
- `dotNetFunction` - .NET委托（支持Action和Func类型）

**返回值：**
- `void` - 无返回值

**说明：**
- 注册.NET函数供Python调用
- **当前版本限制**: 此功能在当前版本中为简化实现，仅输出注册信息，实际功能尚未完全实现
- **预期功能**: 在完整版本中，注册的函数将可以在Python代码中调用

**当前版本行为：**
- 执行Python代码输出注册信息：`注册.NET函数: {pythonFunctionName}`
- 不会实际创建可调用的Python函数

**完整版本预期：**
```csharp
// 注册.NET函数
python.RegisterDotNetFunction("add_numbers", (Func<int, int, int>)((a, b) => a + b));

// 在Python中调用
var result = python.Execute(@"""
result = add_numbers(5, 3)
print(f'Result: {result}')
""");
```

**注意事项：**
- 此功能在当前v1.0.0版本中为占位实现
- 计划在后续版本中完善双向调用功能
- 如需.NET和Python双向调用，建议使用其他成熟的库如Python.NET

### 对象模型支持方法

#### CreateClass

```csharp
public PythonObject CreateClass(string className, string baseClass = "object", 
    Dictionary<string, object> attributes = null, 
    Dictionary<string, string> methods = null)
```

**参数：**
- `className` - 类名
- `baseClass` - 基类名，默认为"object"
- `attributes` - 类属性字典
- `methods` - 类方法字典（方法体为字符串）

**返回值：**
- `PythonObject` - 创建的类对象

**示例：**
```csharp
var attributes = new Dictionary<string, object>
{
    ["default_name"] = "Unknown"
};

var methods = new Dictionary<string, string>
{
    ["greet"] = "def greet(self): return f'Hello, {self.name}!'"
};

var personClass = python.CreateClass("Person", "object", attributes, methods);
```

#### CreateInstance

```csharp
public PythonObject CreateInstance(string className, params object[] constructorArgs)
```

**参数：**
- `className` - 类名
- `constructorArgs` - 构造函数参数

**返回值：**
- `PythonObject` - 创建的实例对象

**错误：**
- 如果类不存在或实例创建失败，会抛出`InvalidOperationException`

**示例：**
```csharp
var person = python.CreateInstance("Person", "Alice", 25);
```

#### DefineClass

```csharp
public void DefineClass(string className, string classDefinition)
```

**参数：**
- `className` - 类名
- `classDefinition` - 完整的类定义代码

**说明：**
- 执行Python代码来定义类
- 适用于复杂的类定义

**示例：**
```csharp
python.DefineClass("Calculator", """
class Calculator:
    def add(self, a, b):
        return a + b
    
    def multiply(self, a, b):
        return a * b
""");
```

#### ImportClass

```csharp
public PythonObject ImportClass(string moduleName, string className)
```

**参数：**
- `moduleName` - 模块名
- `className` - 类名

**返回值：**
- `PythonObject` - 导入的类实例

**说明：**
- 导入Python模块中的类并创建实例

**示例：**
```csharp
var datetimeObj = python.ImportClass("datetime", "datetime");
```

## PythonObject 类

表示Python对象的包装器，支持动态成员访问。

### 核心方法

#### CallMethod

```csharp
public object CallMethod(string methodName, params object[] args)
```

**参数：**
- `methodName` - 方法名
- `args` - 方法参数

**返回值：**
- `object` - 方法调用结果，失败时返回`null`

**示例：**
```csharp
var result = person.CallMethod("greet");
```

#### CallMethod<T>

```csharp
public T CallMethod<T>(string methodName, params object[] args)
```

**参数：**
- `methodName` - 方法名
- `args` - 方法参数

**返回值：**
- `T` - 类型安全的方法调用结果

**示例：**
```csharp
string greeting = person.CallMethod<string>("greet");
```

#### GetProperty

```csharp
public object GetProperty(string propertyName)
```

**参数：**
- `propertyName` - 属性名

**返回值：**
- `object` - 属性值，失败时返回`null`

**示例：**
```csharp
var name = person.GetProperty("name");
```

#### GetProperty<T>

```csharp
public T GetProperty<T>(string propertyName)
```

**参数：**
- `propertyName` - 属性名

**返回值：**
- `T` - 类型安全的属性值

**示例：**
```csharp
string name = person.GetProperty<string>("name");
```

#### SetProperty

```csharp
public bool SetProperty(string propertyName, object value)
```

**参数：**
- `propertyName` - 属性名
- `value` - 要设置的值

**返回值：**
- `bool` - 设置是否成功

**示例：**
```csharp
bool success = person.SetProperty("age", 30);
```

### 动态成员访问

PythonObject实现了`DynamicObject`，支持动态成员访问。**重要说明**：这里的动态绑定是通过C#的`dynamic`关键字实现的，实际访问的是Python对象的属性和方法，而不是C#对象的成员。

```csharp
// 动态属性访问 - 访问的是Python对象的属性
dynamic person = python.CreateInstance("Person", "Bob", 30);
person.name = "Charlie"; // 设置Python对象的name属性
string name = person.name; // 获取Python对象的name属性

// 动态方法调用 - 调用的是Python对象的方法
string greeting = person.greet(); // 调用Python对象的greet方法

// 注意：以下代码访问的是C#对象的属性，不是Python对象的属性
PythonObject personObj = python.CreateInstance("Person", "Bob", 30);
// personObj.name  // 编译错误：PythonObject没有name属性
// personObj.greet() // 编译错误：PythonObject没有greet方法

// 正确的方式是使用dynamic或者类型安全的方法
dynamic dynamicPerson = personObj; // 转换为dynamic
string name = dynamicPerson.name; // 现在可以访问Python属性

// 或者使用类型安全的方法
string name = personObj.GetProperty<string>("name");
string greeting = personObj.CallMethod<string>("greet");
```

**动态绑定的工作原理：**

1. **C# dynamic绑定**：当使用`dynamic`关键字时，C#编译器会在运行时解析成员访问
2. **Python对象访问**：PythonObject重写了`TryGetMember`、`TrySetMember`和`TryInvokeMember`方法
3. **实际执行**：动态访问会转换为对Python对象的属性访问或方法调用

**使用建议：**

- **开发阶段**：使用类型安全的方法（`GetProperty`、`CallMethod`）以获得编译时检查
- **灵活场景**：使用`dynamic`关键字以获得更简洁的语法
- **性能考虑**：`dynamic`访问有运行时开销，类型安全方法性能更好

## 异步任务支持

PyToNet v1.1.0 引入了完整的异步任务支持，所有核心功能都提供了异步版本。

### AsyncPyToNetEngine 类

专门用于异步操作的引擎类，实现了 `IAsyncDisposable` 接口。

#### 构造函数

```csharp
public AsyncPyToNetEngine(PyToNetEngine syncEngine)
```

**参数：**
- `syncEngine` - 同步引擎实例

#### 异步方法

##### ExecuteAsync

```csharp
public async Task<PyToNetResult> ExecuteAsync(string pythonCode)
```

**参数：**
- `pythonCode` - Python代码

**返回值：**
- `Task<PyToNetResult>` - 异步执行结果

**示例：**
```csharp
using var python = new PyToNetEngine();
python.Init("C:\\Python311");

var asyncEngine = python.ToAsync();
var result = await asyncEngine.ExecuteAsync("print('Hello from async!')");
```

##### ExecuteAsync<T>

```csharp
public async Task<PyToNetResult<T>> ExecuteAsync<T>(string pythonCode)
```

**参数：**
- `pythonCode` - Python代码

**返回值：**
- `Task<PyToNetResult<T>>` - 类型安全的异步结果

**示例：**
```csharp
var result = await asyncEngine.ExecuteAsync<int>("5 + 3");
if (result.Success)
{
    int value = result.Value; // value = 8
}
```

##### ExecuteFileAsync

```csharp
public async Task<PyToNetResult> ExecuteFileAsync(string filePath)
```

**参数：**
- `filePath` - Python文件路径

**返回值：**
- `Task<PyToNetResult>` - 异步执行结果

##### CallFunctionAsync

```csharp
public async Task<object> CallFunctionAsync(string moduleName, string functionName, params object[] args)
```

**参数：**
- `moduleName` - 模块名
- `functionName` - 函数名
- `args` - 函数参数

**返回值：**
- `Task<object>` - 异步调用结果

##### CreateInstanceAsync

```csharp
public async Task<PythonObject> CreateInstanceAsync(string className, params object[] constructorArgs)
```

**参数：**
- `className` - 类名
- `constructorArgs` - 构造函数参数

**返回值：**
- `Task<PythonObject>` - 异步创建的Python对象

### 扩展方法

PyToNetEngine 类也提供了扩展方法形式的异步API：

#### InitAsync

```csharp
public static async Task<PyToNetResult> InitAsync(this PyToNetEngine engine, string pythonPath)
```

**示例：**
```csharp
using var python = new PyToNetEngine();
var result = await python.InitAsync("C:\\Python311");
```

#### ExecuteAsync (扩展方法)

```csharp
public static async Task<PyToNetResult> ExecuteAsync(this PyToNetEngine engine, string pythonCode)
```

**示例：**
```csharp
var result = await python.ExecuteAsync("print('Hello')");
```

### 异步使用示例

```csharp
public async Task ProcessDataAsync()
{
    using var python = new PyToNetEngine();
    
    // 异步初始化
    var initResult = await python.InitAsync("C:\\Python311");
    if (!initResult.Success) return;
    
    // 异步执行多个任务
    var task1 = python.ExecuteAsync("import time; time.sleep(2); print('Task 1 done')");
    var task2 = python.ExecuteAsync("import time; time.sleep(1); print('Task 2 done')");
    
    // 等待所有任务完成
    await Task.WhenAll(task1, task2);
    
    Console.WriteLine("所有异步任务完成");
}
```

## 复杂类型处理

PyToNet v1.1.0 增强了复杂类型处理能力，支持JSON序列化和反序列化。

### ComplexTypeConverter 类

提供复杂类型与Python JSON之间的转换功能。

#### ToPythonJson

```csharp
public static string ToPythonJson(object obj)
```

**参数：**
- `obj` - 要转换的对象

**返回值：**
- `string` - Python可识别的JSON字符串

**示例：**
```csharp
var person = new { Name = "Alice", Age = 25 };
string json = ComplexTypeConverter.ToPythonJson(person);
// json = """{"name":"Alice","age":25}"""
```

#### FromPythonJson

```csharp
public static T FromPythonJson<T>(string jsonString)
```

**参数：**
- `jsonString` - JSON字符串

**返回值：**
- `T` - 反序列化的对象

**示例：**
```csharp
string json = "{\"name\":\"Alice\",\"age\":25}";
var person = ComplexTypeConverter.FromPythonJson<Person>(json);
```

### 复杂类型扩展方法

#### ExecuteComplex

```csharp
public static PyToNetResult<T> ExecuteComplex<T>(this PyToNetEngine engine, string pythonCode)
```

**参数：**
- `pythonCode` - Python代码（需要返回JSON字符串）

**返回值：**
- `PyToNetResult<T>` - 复杂类型结果

**示例：**
```csharp
var pythonCode = @"""
import json
person = {"name": "Alice", "age": 25, "active": True}
print(f'JSON_RESULT:{json.dumps(person)}')
""";

var result = python.ExecuteComplex<Person>(pythonCode);
if (result.Success)
{
    Person person = result.Value;
    Console.WriteLine($"姓名: {person.Name}, 年龄: {person.Age}");
}
```

#### ExecuteComplexAsync

```csharp
public static async Task<PyToNetResult<T>> ExecuteComplexAsync<T>(this PyToNetEngine engine, string pythonCode)
```

**参数：**
- `pythonCode` - Python代码（需要返回JSON字符串）

**返回值：**
- `Task<PyToNetResult<T>>` - 异步复杂类型结果

#### CallComplexFunction

```csharp
public static PyToNetResult<T> CallComplexFunction<T>(this PyToNetEngine engine, 
    string moduleName, string functionName, params object[] args)
```

**参数：**
- `moduleName` - 模块名
- `functionName` - 函数名
- `args` - 函数参数（支持复杂类型）

**返回值：**
- `PyToNetResult<T>` - 复杂类型结果

**示例：**
```csharp
var person = new { Name = "Alice", Age = 25 };
var result = python.CallComplexFunction<Person>("my_module", "process_person", person);
```

#### ExecuteWithComplexObject

```csharp
public static PyToNetResult ExecuteWithComplexObject(this PyToNetEngine engine,
    string variableName, object obj, string pythonCode)
```

**参数：**
- `variableName` - Python变量名
- `obj` - 复杂对象
- `pythonCode` - 要执行的Python代码

**返回值：**
- `PyToNetResult` - 执行结果

**示例：**
```csharp
var data = new { Values = new[] { 1, 2, 3, 4, 5 } };
var pythonCode = @"""
import statistics
avg = statistics.mean(data['values'])
print(f'平均值: {avg}')
""";

var result = python.ExecuteWithComplexObject("data", data, pythonCode);
```

### 复杂类型处理示例

#### 完整工作流程

```csharp
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public bool Active { get; set; }
    public List<string> Hobbies { get; set; }
}

// 创建复杂对象
var person = new Person
{
    Name = "Alice",
    Age = 25,
    Active = true,
    Hobbies = new List<string> { "Reading", "Swimming", "Coding" }
};

// 将对象传递给Python
var pythonCode = @"""
print(f'姓名: {person["name"]}')
print(f'年龄: {person["age"]}')
print(f'活跃: {person["active"]}')
print(f'爱好: {person["hobbies"]}')

# 在Python中处理数据
person["age"] += 1
person["hobbies"].append("Painting")

import json
print(f'JSON_RESULT:{json.dumps(person)}')
""";

var result = python.ExecuteWithComplexObject("person", person, pythonCode);

// 从Python获取更新后的对象
if (result.Success)
{
    var updatedPerson = python.ExecuteComplex<Person>("""
import json
print(f'JSON_RESULT:{json.dumps(person)}')
""");
    
    if (updatedPerson.Success)
    {
        Console.WriteLine($"更新后的年龄: {updatedPerson.Value.Age}");
        Console.WriteLine($"更新后的爱好: {string.Join(", ", updatedPerson.Value.Hobbies)}");
    }
}
```

#### 异步复杂类型处理

```csharp
public async Task ProcessComplexDataAsync()
{
    using var python = new PyToNetEngine();
    await python.InitAsync("C:\\Python311");
    
    // 异步执行复杂类型处理
    var result = await python.ExecuteComplexAsync<Person>("""
import json
import time

# 模拟耗时操作
time.sleep(2)

person = {
    "name": "Bob",
    "age": 30,
    "active": True,
    "hobbies": ["Music", "Travel"]
}

print(f'JSON_RESULT:{json.dumps(person)}')
""");
    
    if (result.Success)
    {
        Console.WriteLine($"异步处理完成: {result.Value.Name}");
    }
}
```

## 完整的双向调用支持

PyToNet v1.2.0 引入了完整的双向调用功能，让Python代码可以调用C#中已存在的全局对象。

### 核心概念

双向调用允许：
- **Python调用C#方法** - Python代码可以直接调用C#对象的公共方法
- **C#调用Python函数** - C#代码可以调用Python中注册的函数
- **真正的进程间通信** - 使用命名管道实现，不是简化模拟

### BidirectionalEngine 类

完整的双向调用引擎，使用命名管道实现真正的进程间通信。

#### 构造函数

```csharp
public BidirectionalEngine(string pythonPath, string pythonScript = null)
```

**参数：**
- `pythonPath` - Python解释器路径
- `pythonScript` - 可选，要执行的Python脚本

**示例：**
```csharp
using var engine = new BidirectionalEngine("C:\\Python311");
```

#### RegisterObject

```csharp
public void RegisterObject(string name, object obj)
```

**参数：**
- `name` - 对象名称（在Python中使用）
- `obj` - C#对象实例

**说明：**
- 注册C#对象供Python调用
- 对象的所有公共方法都会自动注册
- Python代码可以通过`对象名.方法名()`调用

**示例：**
```csharp
public class Logger
{
    public void Info(string message) => Console.WriteLine($"[INFO] {message}");
    public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
    public int Add(int a, int b) => a + b;
}

var logger = new Logger();
engine.RegisterObject("logs", logger);

// Python代码可以调用：
// logs.Info("这是来自Python的日志")
// logs.Add(5, 3)
```

#### CallPythonAsync

```csharp
public async Task<T> CallPythonAsync<T>(string method, params object[] args)
```

**参数：**
- `method` - Python函数名
- `args` - 函数参数

**返回值：**
- `Task<T>` - 异步调用结果

**示例：**
```csharp
// 注册Python函数
await engine.ExecutePythonAsync(@"""
def python_add(a, b):
    return a + b

register_python_function("python_add", python_add)
""");

// 调用Python函数
var result = await engine.CallPythonAsync<int>("python_add", 10, 20);
// result = 30
```

#### ExecutePythonAsync

```csharp
public async Task<string> ExecutePythonAsync(string pythonCode)
```

**参数：**
- `pythonCode` - Python代码

**返回值：**
- `Task<string>` - 执行结果

**示例：**
```csharp
var result = await engine.ExecutePythonAsync("print('Hello from Python')");
```

### 双向调用示例

#### 完整的工作流程

```csharp
using PyToNet.Bidirectional;

// 1. 创建双向调用引擎
using var engine = new BidirectionalEngine("C:\\Python311");

// 2. 注册C#对象供Python调用
public class DataProcessor
{
    public string ProcessData(string input)
    {
        return $"Processed: {input.ToUpper()}";
    }
    
    public List<int> FilterNumbers(List<int> numbers, int threshold)
    {
        return numbers.Where(n => n > threshold).ToList();
    }
}

var processor = new DataProcessor();
engine.RegisterObject("processor", processor);

// 3. 注册Python函数供C#调用
await engine.ExecutePythonAsync(@"""
def python_analyze(data):
    """分析数据并返回统计信息"""
    return {
        "count": len(data),
        "sum": sum(data),
        "avg": sum(data) / len(data) if data else 0,
        "max": max(data) if data else 0,
        "min": min(data) if data else 0
    }

register_python_function("analyze", python_analyze)
""");

// 4. Python调用C#方法
await engine.ExecutePythonAsync(@"""
# 调用C#方法
result1 = processor.ProcessData("hello world")
print(f"C#处理结果: {result1}")

numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
result2 = processor.FilterNumbers(numbers, 5)
print(f"过滤结果: {result2}")
""");

// 5. C#调用Python函数
var analysis = await engine.CallPythonAsync<dynamic>("analyze", new[] {1, 2, 3, 4, 5});
Console.WriteLine($"Python分析结果: {analysis}");
```

#### 实际应用场景

**场景1：日志系统集成**
```csharp
// 您现有的C#日志系统
public class AppLogger
{
    public void LogInfo(string message, string category = "General")
    {
        // 您现有的日志实现
        File.AppendAllText($"logs/{DateTime.Today:yyyy-MM-dd}.txt", 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message}{Environment.NewLine}");
    }
    
    public void LogError(Exception ex, string context = "")
    {
        LogInfo($"ERROR: {ex.Message} - {context}", "Error");
    }
}

// 集成到PyToNet
var logger = new AppLogger();
engine.RegisterObject("app_logs", logger);

// Python代码可以使用您现有的日志系统
await engine.ExecutePythonAsync(@"""
app_logs.LogInfo("Python脚本开始执行", "Python")

try:
    # 执行一些操作
    result = some_operation()
    app_logs.LogInfo(f"操作完成: {result}", "Python")
except Exception as e:
    app_logs.LogError(e, "Python脚本执行失败")
""");
```

**场景2：数据交换和处理**
```csharp
// C#端提供数据处理服务
public class DataService
{
    private readonly List<object> _cache = new();
    
    public void CacheData(object data)
    {
        _cache.Add(data);
    }
    
    public List<object> GetCachedData()
    {
        return _cache.ToList();
    }
    
    public string SerializeToJson(object obj)
    {
        return System.Text.Json.JsonSerializer.Serialize(obj);
    }
}

var dataService = new DataService();
engine.RegisterObject("data_service", dataService);

// Python端进行复杂计算
await engine.ExecutePythonAsync(@"""
import json
import numpy as np

def complex_calculation(data):
    """使用Python进行复杂计算"""
    array = np.array(data)
    
    # 复杂的数学运算
    result = {
        "mean": np.mean(array),
        "std": np.std(array),
        "percentiles": np.percentile(array, [25, 50, 75])
    }
    
    # 缓存结果到C#
    data_service.CacheData(result)
    
    return result

register_python_function("complex_calc", complex_calculation)
""");

// C#调用Python进行复杂计算
var data = Enumerable.Range(1, 1000).Select(x => (double)x).ToArray();
var result = await engine.CallPythonAsync<dynamic>("complex_calc", data);

// 从C#获取缓存的结果
var cachedData = dataService.GetCachedData();
```

### 技术架构

#### 通信机制

PyToNet双向调用使用**命名管道（Named Pipes）**实现进程间通信：

1. **C#端**创建`NamedPipeServerStream`
2. **Python端**连接命名管道
3. **RPC协议**定义调用请求和响应格式
4. **序列化**使用JSON进行对象转换

#### 协议格式

**调用请求：**
```json
{
    "id": "call-uuid",
    "method": "object.method",
    "args": [arg1, arg2, ...],
    "timestamp": "2024-01-01T00:00:00Z"
}
```

**调用响应：**
```json
{
    "id": "call-uuid",
    "success": true,
    "result": {...},
    "error": null,
    "timestamp": "2024-01-01T00:00:01Z"
}
```

#### 错误处理

双向调用提供完整的错误处理：
- **调用超时** - 默认30秒超时
- **方法不存在** - 返回明确的错误信息
- **参数类型不匹配** - 类型转换失败时提供详细错误
- **进程通信失败** - 管道断开时自动重连或报错

### 性能优化

#### 异步处理
所有调用都是异步的，避免阻塞主线程：
```csharp
// 并发调用多个Python函数
var task1 = engine.CallPythonAsync<int>("func1");
var task2 = engine.CallPythonAsync<string>("func2");
var task3 = engine.CallPythonAsync<dynamic>("func3");

await Task.WhenAll(task1, task2, task3);
```

#### 对象缓存
频繁调用的对象会被缓存，减少序列化开销。

#### 连接复用
命名管道连接在整个引擎生命周期内保持打开状态。

### 限制和注意事项

1. **对象注册** - 只能注册公共实例方法，静态方法需要单独注册
2. **类型支持** - 支持基本类型和可序列化的复杂类型
3. **进程隔离** - Python在独立进程中运行，内存不共享
4. **性能开销** - 进程间通信有一定性能开销，适合低频调用

## 错误处理最佳实践

### 检查操作结果

```csharp
var result = python.Execute(somePythonCode);
if (!result.Success)
{
    // 根据错误码进行不同的处理
    switch (result.ErrorCode)
    {
        case PyToNetErrorCode.PythonSyntaxError:
            // 处理语法错误
            Console.WriteLine($"语法错误: {result.ErrorMessage}");
            break;
        case PyToNetErrorCode.EnvironmentNotInitialized:
            // 重新初始化环境
            python.Init(pythonPath);
            break;
        default:
            // 通用错误处理
            Console.WriteLine($"错误 [{result.ErrorCode}]: {result.ErrorMessage}");
            break;
    }
}
```

### 使用类型安全的方法

```csharp
try
{
    int result = python.Execute<int>("5 + 3");
    Console.WriteLine($"结果: {result}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"操作失败: {ex.Message}");
}
```

## 性能注意事项

### 1. 环境重用

避免频繁创建和销毁PyToNetEngine实例：

```csharp
// 推荐：重用实例
using (var python = new PyToNetEngine())
{
    python.Init(pythonPath);
    
    // 执行多个操作
    python.Execute("code1");
    python.Execute("code2");
    python.Execute("code3");
}

// 不推荐：频繁创建实例
python1.Execute("code1");
python2.Execute("code2"); // 新的实例
```

### 2. 批量操作

将相关操作合并到单个Python代码块中：

```csharp
// 推荐：批量执行
var code = @"""
x = 5
y = 10
result = x + y
print(f'Result: {result}')
""";
python.Execute(code);

// 不推荐：多次单独执行
python.Execute("x = 5");
python.Execute("y = 10");
python.Execute("result = x + y");
```

### 3. 错误处理优化

在性能关键路径上，使用类型安全的方法避免错误检查开销：

```csharp
// 性能关键路径
try
{
    var result = python.Execute<int>(fastOperation);
    // 直接使用结果
}
catch
{
    // 错误处理
}

// 非性能关键路径
var safeResult = python.Execute(safeOperation);
if (safeResult.Success)
{
    // 使用结果
}
```

## 版本兼容性

### .NET版本要求
- **最低要求**: .NET 8.0
- **推荐版本**: .NET 8.0 或更高版本
- **注意**: 由于使用了最新的C#特性，不支持旧版本的.NET Framework

### Python版本支持
- **最低要求**: Python 3.7
- **推荐版本**: Python 3.11
- **测试版本**: Python 3.7, 3.8, 3.9, 3.10, 3.11
- **注意**: 不同Python版本可能有细微的行为差异，建议使用稳定版本

### 平台支持
- **Windows**: x64, x86 (推荐x64)
- **Linux**: x64 (Ubuntu, CentOS, Debian等主流发行版)
- **macOS**: x64 (macOS 10.15或更高版本)
- **注意**: 跨平台支持基于.NET的跨平台能力，Python环境需要单独安装

## 故障排除

### 常见问题

1. **Python路径错误**
   ```csharp
   // 检查路径是否正确
   var result = python.Init("C:\\Python311");
   if (!result.Success && result.ErrorCode == PyToNetErrorCode.PythonPathNotFound)
   {
       // 提供正确的Python路径
   }
   ```

2. **环境未初始化**
   ```csharp
   // 确保调用Init方法并检查结果
   var initResult = python.Init(pythonPath);
   if (!initResult.Success)
   {
       Console.WriteLine($"环境初始化失败: {initResult.ErrorMessage}");
       return;
   }
   
   // 或者使用异常处理方式
   try
   {
       python.Execute("print('Hello')");
   }
   catch (InvalidOperationException)
   {
       // 环境未初始化，重新初始化
       var retryResult = python.Init(pythonPath);
       if (!retryResult.Success)
       {
           Console.WriteLine($"重新初始化失败: {retryResult.ErrorMessage}");
       }
   }
   ```

3. **语法错误**
   ```csharp
   // 验证Python代码语法
   var result = python.Execute("invalid python code");
   if (!result.Success && result.ErrorCode == PyToNetErrorCode.PythonSyntaxError)
   {
       // 修复语法错误
   }
   ```

### 调试技巧

启用详细日志输出：

```csharp
var result = python.Execute("print('Debug info')");
if (!result.Success)
{
    Console.WriteLine($"错误详情:");
    Console.WriteLine($"  错误码: {result.ErrorCode}");
    Console.WriteLine($"  错误信息: {result.ErrorMessage}");
    Console.WriteLine($"  完整结果: {result}");
}
```

---

**文档版本：** v1.0.0  
**最后更新：** 2026-04-13  
**对应库版本：** PyToNet v1.0.0