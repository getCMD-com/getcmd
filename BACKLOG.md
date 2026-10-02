# Ideas parked until after launch

- Tokeniser: consume heredoc bodies. After `cat <<EOF`, the body lines are currently tokenised as separate commands (over-reports, never hides).
- PowerShell-native verbs (Remove-Item -Recurse, Format-Volume, Stop-Computer, Set-ExecutionPolicy)
- Tokeniser: recognise process substitution `<(...)` / `>(...)`. It is currently read as a redirection whose target starts with `(`, and connectors inside it still split.
