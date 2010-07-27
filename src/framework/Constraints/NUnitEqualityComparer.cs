﻿// ***********************************************************************
// Copyright (c) 2009 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.IO;
using System.Collections;

namespace NUnit.Framework.Constraints
{
    /// <summary>
    /// NUnitEqualityComparer encapsulates NUnit's handling of
    /// equality tests between objects.
    /// </summary>
    public class NUnitEqualityComparer
    {
        #region Static and Instance Fields
        /// <summary>
        /// If true, all string comparisons will ignore case
        /// </summary>
        private bool caseInsensitive;

        /// <summary>
        /// If true, arrays will be treated as collections, allowing
        /// those of different dimensions to be compared
        /// </summary>
        private bool compareAsCollection;

        /// <summary>
        /// If non-zero, equality comparisons within the specified 
        /// tolerance will succeed.
        /// </summary>
        private Tolerance tolerance = Tolerance.Empty;

        /// <summary>
        /// Comparison object used in comparisons for some constraints.
        /// </summary>
        private EqualityAdapter externalComparer;

        /// <summary>
        /// List of points at which a failure occured.
        /// </summary>
        private ObjectList failurePoints;

        private static readonly int BUFFER_SIZE = 4096;
        #endregion

        #region Properties

        /// <summary>
        /// Returns the default NUnitEqualityComparer
        /// </summary>
        public static NUnitEqualityComparer Default
        {
            get { return new NUnitEqualityComparer(); }
        }
        /// <summary>
        /// Gets and sets a flag indicating whether case should
        /// be ignored in determining equality.
        /// </summary>
        public bool IgnoreCase
        {
            get { return caseInsensitive; }
            set { caseInsensitive = value; }
        }

        /// <summary>
        /// Gets and sets a flag indicating that arrays should be
        /// compared as collections, without regard to their shape.
        /// </summary>
        public bool CompareAsCollection
        {
            get { return compareAsCollection; }
            set { compareAsCollection = value; }
        }

        /// <summary>
        /// Gets and sets an external comparer to be used to
        /// test for equality. It is applied to members of
        /// collections, in place of NUnit's own logic.
        /// </summary>
        public EqualityAdapter ExternalComparer
        {
            get { return externalComparer; }
            set { externalComparer = value; }
        }

        /// <summary>
        /// Gets and sets a tolerance used to compare objects of 
        /// certin types.
        /// </summary>
        public Tolerance Tolerance
        {
            get { return tolerance; }
            set { tolerance = value; }
        }

        // TODO: Define some sort of FailurePoint struct or otherwise
        // eliminate the type-unsafeness of the current approach

        /// <summary>
        /// Gets the list of failure points for the last Match performed.
        /// The list consists of objects to be interpreted by the caller.
        /// This generally means that the caller may only make use of
        /// objects it has placed on the list at a particular depthy.
        /// </summary>
        public IList FailurePoints
        {
            get { return failurePoints; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Compares two objects for equality.
        /// </summary>
        public bool ObjectsEqual(object x, object y)
        {
            this.failurePoints = new ObjectList();

            if (x == null && y == null)
                return true;

            if (x == null || y == null)
                return false;

            Type xType = x.GetType();
            Type yType = y.GetType();

            if (xType.IsArray && yType.IsArray && !compareAsCollection)
                return ArraysEqual((Array)x, (Array)y);

            if (x is IDictionary && y is IDictionary)
                return DictionariesEqual((IDictionary)x, (IDictionary)y);

            if (x is ICollection && y is ICollection)
                return CollectionsEqual((ICollection)x, (ICollection)y);

            if (x is IEnumerable && y is IEnumerable && !(x is string && y is string))
                return EnumerablesEqual((IEnumerable)x, (IEnumerable)y);

            if (externalComparer != null)
                return externalComparer.ObjectsEqual(x, y);

            if (x is string && y is string)
                return StringsEqual((string)x, (string)y);

            if (x is Stream && y is Stream)
                return StreamsEqual((Stream)x, (Stream)y);

            if (x is DirectoryInfo && y is DirectoryInfo)
                return DirectoriesEqual((DirectoryInfo)x, (DirectoryInfo)y);

            if (Numerics.IsNumericType(x) && Numerics.IsNumericType(y))
                return Numerics.AreEqual(x, y, ref tolerance);

            if (tolerance != null && tolerance.Value is TimeSpan)
            {
                TimeSpan amount = (TimeSpan)tolerance.Value;

                if (x is DateTime && y is DateTime)
                    return ((DateTime)x - (DateTime)y).Duration() <= amount;

                if (x is TimeSpan && y is TimeSpan)
                    return ((TimeSpan)x - (TimeSpan)y).Duration() <= amount;
            }

            return x.Equals(y);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Helper method to compare two arrays
        /// </summary>
        private bool ArraysEqual(Array x, Array y)
        {
            int rank = x.Rank;

            if (rank != y.Rank)
                return false;

            for (int r = 1; r < rank; r++)
                if (x.GetLength(r) != y.GetLength(r))
                    return false;

            return CollectionsEqual((ICollection)x, (ICollection)y);
        }

        private bool DictionariesEqual(IDictionary x, IDictionary y)
        {
            if (x.Count != y.Count)
                return false;
 
            CollectionTally tally = new CollectionTally(this, x.Keys);
            if (!tally.TryRemove(y.Keys) || tally.Count > 0)
                return false;

            foreach (object key in x.Keys)
                if (!ObjectsEqual(x[key], y[key]))
                    return false;
 
            return true;
        }

        private bool CollectionsEqual(ICollection x, ICollection y)
        {
            IEnumerator expectedEnum = x.GetEnumerator();
            IEnumerator actualEnum = y.GetEnumerator();

            int count;
            for (count = 0; expectedEnum.MoveNext() && actualEnum.MoveNext(); count++)
            {
                if (!ObjectsEqual(expectedEnum.Current, actualEnum.Current))
                    break;
            }

            if (count == x.Count && count == y.Count)
                return true;

            failurePoints.Insert(0, count);
            return false;
        }

        private bool StringsEqual(string x, string y)
        {
            string s1 = caseInsensitive ? x.ToLower() : x;
            string s2 = caseInsensitive ? y.ToLower() : y;

            return s1.Equals(s2);
        }

        private bool EnumerablesEqual(IEnumerable x, IEnumerable y)
        {
            IEnumerator expectedEnum = x.GetEnumerator();
            IEnumerator actualEnum = y.GetEnumerator();

            int count = 0;
            for (; ; )
            {
                bool expectedHasData = expectedEnum.MoveNext();
                bool actualHasData = actualEnum.MoveNext();

                if (!expectedHasData && !actualHasData)
                    return true;

                if (expectedHasData != actualHasData ||
                    !ObjectsEqual(expectedEnum.Current, actualEnum.Current))
                {
                    failurePoints.Insert(0, count);
                    return false;
                }
            }
        }

        /// <summary>
        /// Method to compare two DirectoryInfo objects
        /// </summary>
        /// <param name="x">first directory to compare</param>
        /// <param name="y">second directory to compare</param>
        /// <returns>true if equivalent, false if not</returns>
        private bool DirectoriesEqual(DirectoryInfo x, DirectoryInfo y)
        {
            return x.Attributes == y.Attributes
                && x.CreationTime == y.CreationTime
                && x.FullName == y.FullName
                && x.LastAccessTime == y.LastAccessTime;
        }

        private bool StreamsEqual(Stream x, Stream y)
        {
            if (x == y) return true;

            if (!x.CanRead)
                throw new ArgumentException("Stream is not readable", "expected");
            if (!y.CanRead)
                throw new ArgumentException("Stream is not readable", "actual");
            if (!x.CanSeek)
                throw new ArgumentException("Stream is not seekable", "expected");
            if (!y.CanSeek)
                throw new ArgumentException("Stream is not seekable", "actual");

            if (x.Length != y.Length) return false;

            byte[] bufferExpected = new byte[BUFFER_SIZE];
            byte[] bufferActual = new byte[BUFFER_SIZE];

            BinaryReader binaryReaderExpected = new BinaryReader(x);
            BinaryReader binaryReaderActual = new BinaryReader(y);

            long expectedPosition = x.Position;
            long actualPosition = y.Position;

            try
            {
                binaryReaderExpected.BaseStream.Seek(0, SeekOrigin.Begin);
                binaryReaderActual.BaseStream.Seek(0, SeekOrigin.Begin);

                for (long readByte = 0; readByte < x.Length; readByte += BUFFER_SIZE)
                {
                    binaryReaderExpected.Read(bufferExpected, 0, BUFFER_SIZE);
                    binaryReaderActual.Read(bufferActual, 0, BUFFER_SIZE);

                    for (int count = 0; count < BUFFER_SIZE; ++count)
                    {
                        if (bufferExpected[count] != bufferActual[count])
                        {
                            failurePoints.Insert(0, readByte + count);
                            //FailureMessage.WriteLine("\tIndex : {0}", readByte + count);
                            return false;
                        }
                    }
                }
            }
            finally
            {
                x.Position = expectedPosition;
                y.Position = actualPosition;
            }

            return true;
        }
        #endregion

    }
}
