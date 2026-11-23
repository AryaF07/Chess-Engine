using ChessNEA.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace ChessNEA
{
    /// <summary>
    /// GUI for one game: draws the engine Position, turns mouse clicks into engine moves,
    /// runs the clocks and asks the bot for its move. All chess rules and game-end decisions
    /// come from ChessNEA.Engine; Draw only reads state.
    /// </summary>
    public class Board
    {
        // Screen geometry: a square (col, row) is drawn at X = 165 + 60*col, Y = 5 + 60*row (50x50 sprite);
        // its highlight and click area is X = 160 + 60*col, Y = 60*row (60x60).
        const int BoardLeft = 160, SquareSize = 60;

        public Position Game { get; private set; }
        public Bot bot; // set by Game1 when the bot game mode is chosen
        public bool botGame = false;
        public bool reload = false; //indicates if the textures need to be loaded

        public double duration = 600; //Stores how much time each player has
        double elapsedtimeW = 0; //Time used by white
        double elapsedtimeB = 0; //Time used by black
        public string timerB = "00:00"; //timer for black
        public string timerW = "00:00"; //timer for white

        GameStatus status = GameStatus.Ongoing;
        bool? timeoutLoserIsWhite = null; // set when a clock reaches zero

        readonly Dictionary<(PieceType, bool), Texture2D> pieceSprites = new Dictionary<(PieceType, bool), Texture2D>();
        Texture2D highlightSprite, whitewinscreen, blackwinscreen, stalematescreen, fiftymoverulescreen, insufficientmaterialscreen;
        SpriteFont Font; //Stores font for timer text

        (int Row, int Col)? selected;                          // square of the piece the player clicked
        readonly List<Move> selectedMoves = new List<Move>();  // legal moves of the selected piece
        readonly List<Move> pendingPromotions = new List<Move>(); // the four choices while the promotion menu is open
        static readonly PieceType[] PromotionOrder = { PieceType.Queen, PieceType.Rook, PieceType.Knight, PieceType.Bishop };
        readonly Rectangle[] promotions = new Rectangle[4]; //rectangles for the promotion sprites
        bool leftclickPressed;

        public Board()
        {
            for (int i = 0; i < 4; i++) promotions[i] = new Rectangle(220 + (i * 100), 200, 80, 80);
            SetupBoard();
        }

        public bool IsGameOver => status != GameStatus.Ongoing || timeoutLoserIsWhite != null;

        public void SetupBoard()
        {
            Game = Position.Start();
            status = GameStatus.Ongoing;
            timeoutLoserIsWhite = null;
            elapsedtimeW = 0;
            elapsedtimeB = 0;
            selected = null;
            selectedMoves.Clear();
            pendingPromotions.Clear();
            reload = true;
        }

        public void LoadContent(ContentManager content)
        {
            foreach (bool white in new[] { true, false })
            {
                string suffix = white ? "W" : "B";
                pieceSprites[(PieceType.Pawn, white)] = content.Load<Texture2D>("Pawn" + suffix);
                pieceSprites[(PieceType.Knight, white)] = content.Load<Texture2D>("Knight" + suffix);
                pieceSprites[(PieceType.Bishop, white)] = content.Load<Texture2D>("Bishop" + suffix);
                pieceSprites[(PieceType.Rook, white)] = content.Load<Texture2D>("Rook" + suffix);
                pieceSprites[(PieceType.Queen, white)] = content.Load<Texture2D>("Queen" + suffix);
                pieceSprites[(PieceType.King, white)] = content.Load<Texture2D>("King" + suffix);
            }
            highlightSprite = content.Load<Texture2D>("highlight");
            whitewinscreen = content.Load<Texture2D>("whitewin");
            blackwinscreen = content.Load<Texture2D>("blackwin");
            stalematescreen = content.Load<Texture2D>("stalemate");
            fiftymoverulescreen = content.Load<Texture2D>("50moverule");
            insufficientmaterialscreen = content.Load<Texture2D>("insufficientmaterial");
            Font = content.Load<SpriteFont>("File");
        }

        // ------------------------------------------------------------------ update

        public void Update(GameTime gameTime)
        {
            if (IsGameOver) return;

            UpdateClocks(gameTime);
            if (IsGameOver) return;

            if (botGame && !Game.WhiteToMove)
            {
                Move move = bot.Search(Game).Move;
                if (!move.IsNone) Apply(move);
                return;
            }

            MouseState mouse = Mouse.GetState();
            if (!leftclickPressed && mouse.LeftButton == ButtonState.Pressed) leftclickPressed = true;
            if (leftclickPressed && mouse.LeftButton == ButtonState.Released)
            {
                leftclickPressed = false; // a click is a press followed by a release
                HandleClick(mouse.X, mouse.Y);
            }
        }

        void UpdateClocks(GameTime gameTime)
        {
            double dt = gameTime.ElapsedGameTime.TotalSeconds;
            if (Game.WhiteToMove) elapsedtimeW = Math.Min(duration, elapsedtimeW + dt);
            else elapsedtimeB = Math.Min(duration, elapsedtimeB + dt);
            if (!botGame)
            {
                if (elapsedtimeW >= duration) timeoutLoserIsWhite = true;
                else if (elapsedtimeB >= duration) timeoutLoserIsWhite = false;
            }
            timerW = FormatClock(duration - elapsedtimeW);
            timerB = FormatClock(duration - elapsedtimeB);
        }

        static string FormatClock(double remaining)
        {
            remaining = Math.Max(0, remaining);
            int minutes = (int)(remaining / 60);
            int seconds = (int)(remaining - (minutes * 60));
            return $"{minutes:D2}:{seconds:D2}"; //puts time in MM:SS format
        }

        void HandleClick(int x, int y)
        {
            if (pendingPromotions.Count > 0)
            {
                for (int i = 0; i < 4; i++)
                    if (promotions[i].Contains(x, y))
                    {
                        Apply(pendingPromotions.Find(m => m.Promotion == PromotionOrder[i]));
                        return;
                    }
                return; // the promotion choice must be made before anything else
            }

            if (x < BoardLeft || x >= BoardLeft + 8 * SquareSize || y < 0 || y >= 8 * SquareSize) return;
            int col = (x - BoardLeft) / SquareSize;
            int row = y / SquareSize;

            if (selected != null)
            {
                List<Move> toSquare = selectedMoves.FindAll(m => m.ToRow == row && m.ToCol == col);
                if (toSquare.Count == 1) { Apply(toSquare[0]); return; }
                if (toSquare.Count > 1) // promotion: four moves share the destination
                {
                    pendingPromotions.AddRange(toSquare);
                    return;
                }
            }

            Piece clicked = Game[row, col];
            selectedMoves.Clear();
            selected = null;
            if (clicked != null && clicked.IsWhite == Game.WhiteToMove)
            {
                selected = (row, col);
                foreach (Move m in Game.GenerateLegalMoves())
                    if (m.FromRow == row && m.FromCol == col) selectedMoves.Add(m);
            }
        }

        void Apply(Move move)
        {
            Game.MakeMove(move);
            selected = null;
            selectedMoves.Clear();
            pendingPromotions.Clear();
            status = Game.GetStatus();
        }

        // ------------------------------------------------------------------ draw (reads state only)

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle gameScreen = new Rectangle(125, 150, 564, 186);

            if (!botGame)
            {
                spriteBatch.DrawString(Font, timerW, new Vector2(50, 400), Color.White);
                spriteBatch.DrawString(Font, timerB, new Vector2(50, 50), Color.White);
            }

            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 8; col++)
                {
                    Piece piece = Game[row, col];
                    if (piece != null)
                        spriteBatch.Draw(pieceSprites[(piece.Type, piece.IsWhite)], new Rectangle(165 + 60 * col, 5 + 60 * row, 50, 50), Color.White);
                }

            foreach (Move m in selectedMoves)
                spriteBatch.Draw(highlightSprite, new Rectangle(BoardLeft + SquareSize * m.ToCol, SquareSize * m.ToRow, SquareSize, SquareSize), Color.White * 0.5f);

            switch (status)
            {
                case GameStatus.Checkmate:
                    spriteBatch.Draw(Game.WhiteToMove ? blackwinscreen : whitewinscreen, gameScreen, Color.White);
                    break;
                case GameStatus.Stalemate:
                    spriteBatch.Draw(stalematescreen, gameScreen, Color.White);
                    break;
                case GameStatus.FiftyMoveRule:
                    spriteBatch.Draw(fiftymoverulescreen, gameScreen, Color.White);
                    break;
                case GameStatus.InsufficientMaterial:
                    spriteBatch.Draw(insufficientmaterialscreen, gameScreen, Color.White);
                    break;
                case GameStatus.ThreefoldRepetition: // no dedicated texture: dark panel with text
                    spriteBatch.Draw(highlightSprite, gameScreen, Color.Black);
                    spriteBatch.DrawString(Font, "Draw by threefold repetition", new Vector2(gameScreen.X + 40, gameScreen.Y + 75), Color.White);
                    break;
            }
            if (timeoutLoserIsWhite == true) spriteBatch.Draw(blackwinscreen, gameScreen, Color.White);
            else if (timeoutLoserIsWhite == false) spriteBatch.Draw(whitewinscreen, gameScreen, Color.White);

            if (pendingPromotions.Count > 0)
            {
                bool white = Game.WhiteToMove;
                if (!white) spriteBatch.Draw(highlightSprite, gameScreen, Color.Black);
                for (int i = 0; i < 4; i++)
                    spriteBatch.Draw(pieceSprites[(PromotionOrder[i], white)], promotions[i], Color.White);
            }
        }
    }
}
