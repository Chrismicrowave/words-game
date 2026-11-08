using System;
using System.Collections.Generic;

namespace Words.Community
{
    /// <summary>Immutable gameplay projection of one cached remote content version.</summary>
    public sealed class CommunityWordListProvider : IWordListProvider
    {
        private readonly WordListLibraryContent content;
        public CommunityItemKey Key { get; }
        public CommunityContentVersion Version { get; }
        public string DisplayName { get; }
        public LanguageMode LanguageMode => content.LanguageMode;
        public bool IsEditable => false;

        public CommunityWordListProvider(CommunityItemKey key, CommunityContentVersion version,
            string displayName, WordListLibraryContent content)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Community word list" : displayName.Trim();
            this.content = content != null ? content.Clone() : throw new ArgumentNullException(nameof(content));
        }

        public List<string> GetWords() => content.Words != null ? new List<string>(content.Words) : new List<string>();
        public List<ChineseWordEntry> GetChineseWords() => LevelWordListProvider.CloneChineseWords(content.ChineseWords);
        public List<MixedWordEntry> GetMixedWords() => LevelWordListProvider.CloneMixedWords(content.MixedWords);
    }
}
