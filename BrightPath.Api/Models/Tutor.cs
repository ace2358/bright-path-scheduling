namespace BrightPath.Api.Models;

public class Tutor
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public ICollection<Lesson> Lessons { get; set; } = [];
}
