namespace WinBoard.Core;

/// <summary>
/// Prefix trie over folded dictionary words. The beam walks this; a neural
/// spatial module can reuse it without a decoder rewrite.
/// </summary>
public sealed class WordTrie
{
    public sealed class Node
    {
        public Dictionary<char, Node> Next { get; } = new();

        public List<WordEntry>? Ends { get; set; }
    }

    public Node Root { get; } = new();

    public int WordCount { get; private set; }

    public static WordTrie Build(WordList words)
    {
        var trie = new WordTrie();
        foreach (WordEntry entry in words.All)
        {
            Node node = trie.Root;
            foreach (char c in entry.Folded)
            {
                if (!node.Next.TryGetValue(c, out Node? child))
                {
                    child = new Node();
                    node.Next[c] = child;
                }

                node = child;
            }

            node.Ends ??= [];
            node.Ends.Add(entry);
            trie.WordCount++;
        }

        return trie;
    }

    public bool Contains(char[] folded)
    {
        Node node = Root;
        foreach (char c in folded)
        {
            if (!node.Next.TryGetValue(c, out Node? child))
            {
                return false;
            }

            node = child;
        }

        return node.Ends is { Count: > 0 };
    }
}
