namespace Getcmd.Core;

// Ordered by severity: a command's level is the highest level of its parts.
public enum Level { Read, Mutate, Egress, Secrets, Destructive }

public enum Action { Allow, Ask, Block }
