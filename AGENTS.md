### Project Member Arrangement Style
- Members in classes must follow this order:
    1. Fields (private and protected)
    2. Properties (public, protected, then private)
    3. Methods (public, protected, then private)

So put all public methods first and only then private, don't mix them.

### Style Guide
- Naming convention is PascalCase with no preceding underscores. Except for private variables and locals start with a lowercase letter (camelCase). This is the common Microsoft C# naming style.
- Variables should be named with descriptive names instead of single letters or abbreviations. The only exception is LINQ expressions should use single letter names.
- Another exception is event handlers and other common names like `e` for exception and `e` for event args. Use `ex` for event variable only when `e` would conflict.
- Prefer preincrement when possible (`++i` instead of `i++`)
- Do not prefix local variables with `_` or `this.`
- The last item in a list should have a trailing comma (applies to enums, lists, etc.):

```cs
public enum ExampleEnum
{
    None,
    Item1,
    SomeOtherItem,
}
```

- ASP.NET Controller paths should be in this format: `api/v1/ImageUpload/create` (rather than using "-" to separate words)
- Controller classes should end with `Controller` but not put that in the API path.
- In Avalonia .axaml files the two-way binding is the default. So do not use `Mode=TwoWay` as it is unnecessary. Only specify one way binding if needed for a use case.

- Do not use `[ObservableProperty]`. Instead, use `set => SetProperty(ref field, value);` to properly notify the UI of changes.
- Make sure view models inherit from ViewModelBase otherwise the above approach won't work.

- Using `[RelayCommand]` will probably blow up the application build so only use it as an absolute last resort. Usually just use bindings to call normal methods and it works fine.

### Command and Event Handling
- `IReactiveCommand` (from ReactiveUI) is NOT used.
- Avalonia `.axaml` files should directly call view model methods or use `RelayCommand` from `CommunityToolkit.Mvvm` 
- Do **not** use `IRelayCommand`
- Example binding: `<Button Command="{Binding SaveChanges}" />` where `SaveChanges` is a method in the ViewModel.

### Using ViewModels
- New ViewModels are preferred to be created rather than using complex parent references in axaml files. A simple ViewModel with straightforward property bindings and function calls is nice.
- Simple ViewModels that are used once can share the parent view's file, more complex view models should get their own file.
- ViewModels should inherit ViewModelBase
- New ViewModels should have a dependency injection constructor (marked with `[ActivatorUtilitiesConstructor]`) and a design-time parameterless constructor
- When providing a file picker to the user, the ViewModel requires help from the code-behind of the window. See `OpenFilePicker` as an example method.

### New language features
- `field` keyword should be used

Example simple property:
```cs
    public string ExampleProperty
    {
        get;
        set => SetProperty(ref field, value);
    } = "Initial value";
```

### Project Technologies

The project uses Avalonia for a GUI subproject. ASP.NET Core is used for the server. Backend project is separated from the DualView.Server project for clarity 
And there is a separate web UI for this app as a Blazor subproject called DualView.Client.

To generate migrations, run the following command: 
`dotnet ef --project Backend/Backend.csproj migrations add MigrationNameHere`

### Structure

Database access goes through on the client `IClientDatabaseService` and on the server `IDatabaseService`. 
There is also a common `IDatabaseCommonService` service. To decide where to put a new method for getting data, 
it is important to consider the following:
- If the data is only needed on the server, then add a method to `IDatabaseService`
- If the data is needed on the client / updated from the client, but the data does not use DTO objects, then put it in `IDatabaseCommonService` as the server database inherits the common one that makes it also available there effortlessly.
- The last case is that if the data is used on the client, and it uses DTO objects, then put it in `IClientDatabaseService`. 
  So only put methods primarily there that work with DTO objects, for example, if the methods just take in ID numbers, then it belongs in the common service.

As `DatabaseService` implementation implements the client database as well, it needs wrapper methods for all added client methods (build will fail due to this so you will notice), 
but proactively putting things in the right place (like `IDatabaseCommonService`) makes the build issues much easier to fix.

When making calls from the client that aren't really about modifying database data, but triggering some other stuff, use the `IBackendAPI` interface instead to make general operations and access them on the clients.

Database access should be implemented through Entity Framework, except for the case of very heavy duty access like tag parsing which can benefit from direct SQL reading to avoid extra overhead on the hot path.
