namespace SearchService
{
    public sealed record MovieSearchRequest(string City, DateOnly Date);

    public sealed record MovieCard(
        long MovieId,
        string Title,
        string? Language,
        int? RuntimeMin,
        string[] Genres,
        DateTimeOffset Showtime
    );
}
