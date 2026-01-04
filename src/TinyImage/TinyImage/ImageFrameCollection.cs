using System;
using System.Collections;
using System.Collections.Generic;

namespace TinyImage;

/// <summary>
/// Represents a collection of image frames.
/// For single-frame formats, this collection contains exactly one frame.
/// For animated formats, this collection contains multiple frames with timing information.
/// </summary>
public sealed class ImageFrameCollection : IReadOnlyList<ImageFrame>
{
    private readonly List<ImageFrame> _frames;

    /// <summary>
    /// Gets the root (first) frame of the image.
    /// This is the primary frame used for single-frame operations.
    /// </summary>
    public ImageFrame RootFrame => _frames[0];

    /// <summary>
    /// Gets the number of frames in the collection.
    /// </summary>
    public int Count => _frames.Count;

    /// <summary>
    /// Gets the frame at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the frame.</param>
    /// <returns>The frame at the specified index.</returns>
    public ImageFrame this[int index] => _frames[index];

    /// <summary>
    /// Creates a new frame collection with a single frame of the specified dimensions.
    /// </summary>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    internal ImageFrameCollection(int width, int height)
    {
        _frames = new List<ImageFrame> { new ImageFrame(width, height) };
    }

    /// <summary>
    /// Creates a new frame collection with the specified root frame.
    /// </summary>
    /// <param name="rootFrame">The initial root frame.</param>
    internal ImageFrameCollection(ImageFrame rootFrame)
    {
        if (rootFrame == null)
            throw new ArgumentNullException(nameof(rootFrame));
        _frames = new List<ImageFrame> { rootFrame };
    }

    /// <summary>
    /// Creates a new frame collection from an existing list of frames.
    /// </summary>
    /// <param name="frames">The frames to include in the collection.</param>
    internal ImageFrameCollection(IEnumerable<ImageFrame> frames)
    {
        if (frames == null)
            throw new ArgumentNullException(nameof(frames));
        
        _frames = new List<ImageFrame>(frames);
        
        if (_frames.Count == 0)
            throw new ArgumentException("Frame collection must contain at least one frame.", nameof(frames));
    }

    /// <summary>
    /// Adds a frame to the end of the collection.
    /// </summary>
    /// <param name="frame">The frame to add.</param>
    /// <exception cref="ArgumentNullException">Frame is null.</exception>
    public void AddFrame(ImageFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        _frames.Add(frame);
    }

    /// <summary>
    /// Creates and adds a new frame with the specified dimensions.
    /// </summary>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    /// <returns>The newly created frame.</returns>
    public ImageFrame AddFrame(int width, int height)
    {
        var frame = new ImageFrame(width, height);
        _frames.Add(frame);
        return frame;
    }

    /// <summary>
    /// Inserts a frame at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which to insert the frame.</param>
    /// <param name="frame">The frame to insert.</param>
    /// <exception cref="ArgumentNullException">Frame is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Index is out of range.</exception>
    public void InsertFrame(int index, ImageFrame frame)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        _frames.Insert(index, frame);
    }

    /// <summary>
    /// Removes the frame at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the frame to remove.</param>
    /// <exception cref="InvalidOperationException">Cannot remove the last frame.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Index is out of range.</exception>
    public void RemoveFrame(int index)
    {
        if (_frames.Count == 1)
            throw new InvalidOperationException("Cannot remove the last frame from the collection.");
        _frames.RemoveAt(index);
    }

    /// <summary>
    /// Removes all frames except the root frame.
    /// </summary>
    public void ClearExceptRoot()
    {
        if (_frames.Count > 1)
        {
            var root = _frames[0];
            _frames.Clear();
            _frames.Add(root);
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the frames.
    /// </summary>
    public IEnumerator<ImageFrame> GetEnumerator() => _frames.GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through the frames.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Creates a deep copy of this frame collection.
    /// </summary>
    /// <returns>A new collection with cloned frames.</returns>
    internal ImageFrameCollection Clone()
    {
        var clonedFrames = new List<ImageFrame>(_frames.Count);
        foreach (var frame in _frames)
        {
            clonedFrames.Add(frame.Clone());
        }
        return new ImageFrameCollection(clonedFrames);
    }
}
