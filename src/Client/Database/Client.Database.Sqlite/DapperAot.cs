using Dapper;

// Every Dapper call in this assembly gets generated code instead of Reflection.Emit, which NativeAOT lacks
[module: DapperAot]
