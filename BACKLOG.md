# Ideas parked until after launch

- Tokeniser: consume heredoc bodies. After `cat <<EOF`, the body lines are currently tokenised as separate commands (over-reports, never hides).
- Tokeniser: recognise process substitution `<(...)` / `>(...)`. It is currently read as a redirection whose target starts with `(`, and connectors inside it still split.
