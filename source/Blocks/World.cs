﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Kyoob.Effects;
using Kyoob.Terrain;

#warning TODO : Use something like Octree<BoundingBox> or Octree<Chunk> for querying visible sphere.

namespace Kyoob.Blocks
{
    /// <summary>
    /// Creates a new world.
    /// </summary>
    public class World : IDisposable
    {
        /// <summary>
        /// The magic number for worlds. (FourCC = 'WRLD')
        /// </summary>
        private const int MagicNumber = 0x444C5257;
        
        private Stopwatch _watch;
        private GraphicsDevice _device;
        private BaseEffect _effect;
        private SpriteSheet _spriteSheet;
        private Dictionary<Index3D, Chunk> _chunks;
        private TerrainGenerator _terrain;

        private int     _drawCount  = 0;
        private int     _frameCount = 0;
        private double  _timeCount  = 0.0;
        private double  _tickCount  = 0.0;

        /// <summary>
        /// Gets the graphics device this world is on.
        /// </summary>
        public GraphicsDevice GraphicsDevice
        {
            get
            {
                return _device;
            }
        }

        /// <summary>
        /// Gets the world's sprite sheet.
        /// </summary>
        public SpriteSheet SpriteSheet
        {
            get
            {
                return _spriteSheet;
            }
        }

        /// <summary>
        /// Gets this world's terrain generator.
        /// </summary>
        public TerrainGenerator TerrainGenerator
        {
            get
            {
                return _terrain;
            }
        }

        /// <summary>
        /// Creates a new world.
        /// </summary>
        /// <param name="device">The graphics device.</param>
        /// <param name="effect">The base effect.</param>
        /// <param name="spriteSheet">The sprite sheet to use with each cube.</param>
        /// <param name="terrain">The terrain generator to use.</param>
        public World( GraphicsDevice device, BaseEffect effect, SpriteSheet spriteSheet, TerrainGenerator terrain )
        {
            // set variables
            _device = device;
            _effect = effect;
            _spriteSheet = spriteSheet;
            _chunks = new Dictionary<Index3D, Chunk>();
            _terrain = terrain;

            _creationThread = new Thread( new ThreadStart( ChunkCreationThread ) );
            _creationThread.Start();

            /*
            // add some arbitrary chunks
            for ( int x = -3; x <= 3; ++x )
            {
                for ( int y = -1; y <= 1; ++y )
                {
                    for ( int z = -3; z <= 3; ++z )
                    {
                        CreateChunk( x, y, z );
                    }
                }
            }
            */
        }

        /// <summary>
        /// Creates a new world by loading it from a stream.
        /// </summary>
        /// <param name="bin">The binary reader to use when reading the world.</param>
        /// <param name="device">The graphics device.</param>
        /// <param name="effect">The base effect.</param>
        /// <param name="spriteSheet">The sprite sheet to use with each cube.</param>
        /// <param name="terrain">The terrain generator to use.</param>
        private World( BinaryReader bin, GraphicsDevice device, BaseEffect effect, SpriteSheet spriteSheet, TerrainGenerator terrain )
        {
            // set variables
            _device = device;
            _effect = effect;
            _spriteSheet = spriteSheet;
            _chunks = new Dictionary<Index3D, Chunk>();
            _terrain = terrain;

            // load the seed (and terrain) and chunks
            _terrain.Seed = bin.ReadInt32();
            int count = bin.ReadInt32();
            for ( int i = 0; i < count; ++i )
            {
                // read the index, then the chunk, then record both
                Index3D index = new Index3D( bin.ReadInt32(), bin.ReadInt32(), bin.ReadInt32() );
                Chunk chunk = Chunk.ReadFrom( bin.BaseStream, this );
                if ( chunk != null )
                {
                    _chunks.Add( index, chunk );
                }
            }
        }

        /// <summary>
        /// Creates a chunk with the given indices.
        /// </summary>
        /// <param name="x">The X index.</param>
        /// <param name="y">The Y index.</param>
        /// <param name="z">The Z index.</param>
        private void CreateChunk( int x, int y, int z )
        {
            lock ( _chunks )
            {
                // create the chunk index
                Index3D index = new Index3D( x, y, z );

                // make sure we don't already have that chunk created
                if ( _chunks.ContainsKey( index ) )
                {
                    return;
                }

                // create the chunk and store it
                Chunk chunk = new Chunk( this, new Vector3(
                    x * 8.0f,
                    y * 8.0f,
                    z * 8.0f
                ) );
                _chunks.Add( index, chunk );
            }
        }



        private Thread _creationThread;

        private void ChunkCreationThread()
        {
            for ( int x = -3; x <= 3; ++x )
            {
                for ( int y = -3; y <= 3; ++y )
                {
                    for ( int z = -3; z <= 3; ++z )
                    {
                        CreateChunk( x, y, z );
                    }
                }
            }
            _creationThread.Join( 1000 );
            Terminal.WriteLine( Color.Cyan, "World creation complete." );
        }



        /// <summary>
        /// Disposes of this world, including all of the chunks in it.
        /// </summary>
        public void Dispose()
        {
            _creationThread.Join( 10 );
            foreach ( Chunk chunk in _chunks.Values )
            {
                chunk.Dispose();
            }
        }

        /// <summary>
        /// Converts a chunk's local coordinates to world coordinates.
        /// </summary>
        /// <param name="center">The center of the chunk.</param>
        /// <param name="x">The X index.</param>
        /// <param name="y">The Y index.</param>
        /// <param name="z">The Z index.</param>
        /// <returns></returns>
        public Vector3 ChunkToWorld( Vector3 center, int x, int y, int z )
        {
            return new Vector3(
                center.X + ( x - 8 ),
                center.Y + ( y - 8 ),
                center.Z + ( z - 8 )
            );
        }

        /// <summary>
        /// Draws the world.
        /// </summary>
        /// <param name="gameTime">Frame time information.</param>
        /// <param name="camera">The current camera to use for getting visible tiles.</param>
        public void Draw( GameTime gameTime, Camera camera )
        {
            // time how long it takes to draw our chunks
            _watch = Stopwatch.StartNew();
            int count = 0;
            lock ( _chunks )
            {
                foreach ( Chunk chunk in _chunks.Values )
                {
                    if ( !camera.CanSee( chunk.Bounds ) )
                    {
                        continue;
                    }
                    chunk.Draw( _effect );
                    ++count;
                }
            }
            _watch.Stop();


            // update our average chunk drawing information
            ++_frameCount;
            _drawCount += count;
            _tickCount += gameTime.ElapsedGameTime.TotalSeconds;
            _timeCount += _watch.Elapsed.TotalMilliseconds;
            if ( _tickCount >= 1.0 )
            {
                Terminal.WriteLine( Color.Yellow,
                    "{0:0.00} chunks in {1:0.00}ms",
                    (float)_drawCount / _frameCount,
                           _timeCount / _frameCount
                );

                _frameCount = 0;
                _tickCount -= 1.0;
                _timeCount = 0.0;
                _drawCount = 0;
            }
        }

        /// <summary>
        /// Saves this world to a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        public void SaveTo( Stream stream )
        {
            // create helper writer and write the magic number
            BinaryWriter bin = new BinaryWriter( stream );
            bin.Write( MagicNumber );

            // save the noise seed and number of chunks and then each chunk
            bin.Write( _terrain.Seed );
            bin.Write( _chunks.Count );
            foreach ( Index3D key in _chunks.Keys )
            {
                // write the index
                bin.Write( key.X );
                bin.Write( key.Y );
                bin.Write( key.Z );

                // write the chunk
                _chunks[ key ].SaveTo( stream );
            }
        }

        /// <summary>
        /// Reads a world's data from a stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="device">The graphics device to create the world on.</param>
        /// <param name="effect">The effect to use when rendering the world.</param>
        /// <param name="spriteSheet">The world's sprite sheet.</param>
        /// <param name="terrain">The terrain generator to use.</param>
        public static World ReadFrom( Stream stream, GraphicsDevice device, BaseEffect effect, SpriteSheet spriteSheet, TerrainGenerator terrain )
        {
            // create our helper reader and make sure we find the world's magic number
            BinaryReader bin = new BinaryReader( stream );
            if ( bin.ReadInt32() != MagicNumber )
            {
                Terminal.WriteLine( Color.Red, "Encountered invalid world in stream." );
                return null;
            }

            // now try to read the world
            try
            {
                World world = new World( bin, device, effect, spriteSheet, terrain );
                return world;
            }
            catch ( Exception ex )
            {
                Terminal.WriteLine( Color.Red, "Failed to load world." );
                Terminal.WriteLine( Color.Red, "-- {0}", ex.Message );
                // Terminal.WriteLine( ex.StackTrace );

                return null;
            }
        }
    }
}