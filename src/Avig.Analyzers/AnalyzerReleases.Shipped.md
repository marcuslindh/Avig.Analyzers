; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AVIG0001 | Formatting | Info | BlankLineAfterClosingBraceAnalyzer. Has a code fix.
AVIG0002 | Naming | Info | SingleCharacterIdentifierAnalyzer
AVIG0003 | Formatting | Info | BlankLineBeforeReturnAnalyzer. Has a code fix.
AVIG0004 | Formatting | Info | BlankLineBeforeTryAnalyzer. Has a code fix.
AVIG0005 | Formatting | Info | BlankLineBeforeWhileAnalyzer. Has a code fix.
AVIG0006 | Formatting | Disabled | CrlfLineEndingAnalyzer. Opt-in only; has a code fix.
AVIG0007 | Design | Info | NullableReturnAnalyzer
AVIG0008 | Design | Info | RepositoryCancellationTokenAnalyzer
AVIG0009 | Documentation | Info | RequiredXmlDocumentationAnalyzer
AVIG0010 | Design | Warning | AuthMagicStringAnalyzer
AVIG0011 | Documentation | Info | SummaryRequiredWithParamDocsAnalyzer
AVIG0012 | Security | Info | ExplicitAuthorizationDecisionAnalyzer
