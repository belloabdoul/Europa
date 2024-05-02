using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;
// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities
{
    public class File
    {
        [Key]
        [StringLength(64)]
        public string Id { get; set; }

        // The full path to the file
        [NotMapped]
        public string Path { get; init; }

        // Size of the file
        [NotMapped]
        public long Size { get; init; }

        // The type of the file
        [NotMapped]
        public FileType FileType { get; init; }

        // The last time the file has been modified
        [NotMapped]
        public DateTime DateModified { get; init; }

        // The perceptual hash of the file if it is an image
        public Vector? ImageHash { get; set; }
        
        public File() { }

        // Constructors
        public File(FileInfo file)
        {
            Id = string.Empty;
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName).ToUniversalTime();
            FileType = FileType.None;
        }

        public File(FileInfo file, string hash)
        {
            Id = hash;
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName).ToUniversalTime();
            FileType = FileType.None;
        }

        public File(File file)
        {
            Id = file.Id;
            Size = file.Size;
            Path = file.Path;
            DateModified = file.DateModified;
            FileType = file.FileType;
            ImageHash = file.ImageHash;
        }

        // Relationships
        public ICollection<Similarity> HypotheticalDuplicates { get; init; } = [];
        public ICollection<Similarity> HypotheticalOriginals { get; init; } = [];
    }
}