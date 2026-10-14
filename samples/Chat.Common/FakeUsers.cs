namespace Chat;

public static class FakeUsers
{
    public static IReadOnlyList<User> All { get; } =
    [
        new User(1, "alice"),
        new User(2, "bob"),
        new User(3, "carol"),
        new User(4, "dave"),
        new User(5, "erin"),
        new User(6, "frank"),
    ];
}
