using Grimoire.Hub.Cli;

// Every invocation — web server included — is dispatched by the Spectre CommandApp
// (ADR-020, amended): starting the server is what the default command HubRootCommand does
// when no command name is given, so the hand-written "CLI or web host?" gate that used to
// live here is gone. Spectre's own parser now owns that decision, which is also what makes
// the path switches appear in the root help's OPTIONS section natively.
return await HubCliApp.RunAsync(args);
