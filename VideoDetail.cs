using System;

namespace MusicList
{
    public record VideoDetail
    {
        public string Title { get; init; }
        public string Id { get; init; }
        public string ThumbnailUrl { get; init; }
        public TimeSpan Duration { get; init; }
    }
}
