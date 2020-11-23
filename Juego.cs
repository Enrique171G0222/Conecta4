using GalaSoft.MvvmLight.Command;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Conecta4
{
    public enum Opcion { COL1 = 1, COL2 = 2, COL3 = 3, COL4 = 4, COL5 = 5, COL6 = 6, COL7 = 7 }
    public enum Comando { NombreEnviado, JugadaEnviada }
    public enum TipoFicha { FICHA_AMARILLA = 0, FICHA_ROJA = 1, FICHA_VACIA = -1 }
    public class Ficha : INotifyPropertyChanged
    {
        private int color;
        public int Color
        {
            get
            {
                return color;
            }
            set
            {
                if (color.Equals(value)) return;
                color = value;
                AlCambiar("Color");
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void AlCambiar(string propiedad)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
    }

    public partial class Juego : INotifyPropertyChanged
    {
        private int renglones = 6;
        private int columnas = 7;
        private List<Ficha> listaFichas;
        public List<Ficha> ListaFichas
        {
            get
            {
                return listaFichas;
            }
            set
            {
                listaFichas = value;
                AlCambiar("Fichas");
            }
        }

        //Propiedades      
        public string Jugador1 { get; set; } = "Jugador";
        public string Jugador2 { get; set; }
        public string IP { get; set; } = "localhost";
        public bool MainWindowVisible { get; set; } = true;
        public string Mensaje { get; set; }
        private bool puedeJugar;
        public bool PuedeJugar
        {
            get
            {
                return puedeJugar;
            }
            set
            {
                puedeJugar = value;
                AlCambiar("PuedeJugar");
            }
        }
        public Opcion? SeleccionJugador1 { get; set; }
        public Opcion? SeleccionJugador2 { get; set; }

        //comandos
        public ICommand JugarCommand { get; set; }
        public ICommand IniciarCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void AlCambiar(string propiedad)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
        HttpListener servidor;
        ClientWebSocket cliente;
        Dispatcher currentDispatcher;
        WebSocket webSocket;

        //ventanas
        Lobby lobby;
        VentanaJuego juego;


        public Juego()
        {
            currentDispatcher = Dispatcher.CurrentDispatcher;
            IniciarCommand = new RelayCommand<bool>(IniciarPartida);
            JugarCommand = new RelayCommand<string>(Jugar);
        }
        private void Jugar(string obj)
        {
            try
            {
                if (cliente != null)//soy cliente
                {
                    SeleccionJugador2 = (Opcion)Enum.Parse(typeof(Opcion), obj);
                    int c = AsignarColumna(SeleccionJugador2);
                    Ficha f = RevisarEspaciosBlancos(c);
                    f.Color = (int)TipoFicha.FICHA_ROJA;
                    EnviarComando(new DatoEnviado { Comando = Comando.JugadaEnviada, DatoJugada = SeleccionJugador2 });
                }
                else //soy servidor
                {
                    SeleccionJugador1 = (Opcion)Enum.Parse(typeof(Opcion), obj);
                    int c = AsignarColumna(SeleccionJugador1);
                    Ficha f = RevisarEspaciosBlancos(c);
                    f.Color = (int)TipoFicha.FICHA_AMARILLA;
                    EnviarComando(new DatoEnviado { Comando = Comando.JugadaEnviada, DatoJugada = SeleccionJugador1 });
                }
                PuedeJugar = false;
                CambiarMensaje("Esperando la jugada del adversario");

                _ = VerificarGanadorAsync();
            }
            catch (Exception)
            {
                MessageBox.Show("La columna donde quiere colocar la ficha esta llena");
            }
        }
        private void PrepararTablero()
        {
            ListaFichas = Enumerable.Range(1, renglones * columnas).Select(a => new Ficha() { Color = (int)TipoFicha.FICHA_VACIA }).ToList();
        }

        private void Lobby_Closing(object sender, CancelEventArgs e)
        {
            MainWindowVisible = true;
            Actualizar("MainWindowVisible");

            if (servidor != null)
            {
                servidor.Stop();
                servidor = null;
            }
        }
        private async void IniciarPartida(bool tipoPartida)
        {
            try

            {
                MainWindowVisible = false;
                lobby = new Lobby();
                lobby.Closing += Lobby_Closing;
                lobby.DataContext = this;
                lobby.Show();
                Actualizar();

                if (tipoPartida == true)
                {
                    CrearPartida();
                }
                else
                {
                    Jugador2 = Jugador1;
                    Jugador1 = null;
                    Mensaje = "Intentando conectar con el servidor en " + IP;
                    Actualizar("Mensaje");
                    await ConectarPartida();
                }


            }
            catch (Exception ex)
            {
                Mensaje = ex.Message;
                Actualizar();
            }

        }
        public void CrearPartida()
        {
            servidor = new HttpListener();
            servidor.Prefixes.Add("http://*:1000/conecta4/");
            servidor.Start();
            servidor.BeginGetContext(OnContext, null);

            Mensaje = "Esperando que se conecte un adversario...";
            Actualizar();
        }
        public async Task ConectarPartida()
        {
            cliente = new ClientWebSocket();
            await cliente.ConnectAsync(new Uri($"ws://{IP}:1000/conecta4/"), CancellationToken.None);

            webSocket = cliente;

            RecibirComando();
        }
        private async void OnContext(IAsyncResult ar)
        {
            var context = servidor.EndGetContext(ar);

            if (context.Request.IsWebSocketRequest)
            {
                var listener = await context.AcceptWebSocketAsync(null);
                webSocket = listener.WebSocket;

                CambiarMensaje("Cliente aceptado. Esperando información del jugador.");

                //Enviar mis datos al cliente
                EnviarComando(new DatoEnviado { Comando = Comando.NombreEnviado, DatoJugada = Jugador1 });

                RecibirComando();
            }
            else
            {

                context.Response.StatusCode = 404;
                context.Response.Close();
                servidor.BeginGetContext(OnContext, null);
            }
        }
        //Comprime el comando a json
        private async void EnviarComando(DatoEnviado datos)
        {
            byte[] buffer;
            var json = JsonConvert.SerializeObject(datos);
            buffer = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        //Ventana juego prepara para empezar a jugar
        private async void RecibirComando()
        {
            try
            {
                byte[] buffer = new byte[1024];

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        juego.Close();
                        return;
                    }

                    string datosRecibidos = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    var comando = JsonConvert.DeserializeObject<DatoEnviado>(datosRecibidos);

                    if (cliente != null) //Soy cliente
                    {
                        switch (comando.Comando)
                        {
                            case Comando.NombreEnviado:
                                Jugador1 = (string)comando.DatoJugada;
                                CambiarMensaje("Conectado con el jugador " + Jugador1);
                                //Esto es antes de abrir la ventana del juego
                                _ = currentDispatcher.BeginInvoke(new Action(() =>
                                {
                                    EnviarComando(new DatoEnviado { Comando = Comando.NombreEnviado, DatoJugada = Jugador2 });
                                    lobby.Hide();
                                    juego = new VentanaJuego();
                                    juego.Title = "CLIENTE";
                                    juego.DataContext = this;
                                    PrepararTablero();

                                    CambiarMensaje("Seleccione la opción con la cual quiera jugar");

                                    juego.ShowDialog();
                                    lobby.Show();
                                }));

                                break;
                            //Estos son los datos
                            case Comando.JugadaEnviada:
                                currentDispatcher.Invoke(new Action(() =>
                                {
                                    SeleccionJugador1 = (Opcion)(long)comando.DatoJugada;
                                    int c = AsignarColumna(SeleccionJugador1);
                                    Ficha f = RevisarEspaciosBlancos(c);
                                    f.Color = (int)TipoFicha.FICHA_AMARILLA;
                                    CambiarMensaje("El adversario ha seleccionado su jugada.");
                                    PuedeJugar = true;
                                }));

                                _ = VerificarGanadorAsync();

                                break;
                        }
                    }
                    else //Soy servidor
                    {
                        switch (comando.Comando)
                        {
                            case Comando.NombreEnviado:
                                Jugador2 = (string)comando.DatoJugada;
                                CambiarMensaje("Conectado con el jugador " + Jugador2);
                                //Esto es antes dee abrir la ventana del juego y para abrirla
                                _ = currentDispatcher.BeginInvoke(new Action(() =>
                                {
                                    lobby.Hide();
                                    VentanaJuego juego = new VentanaJuego();
                                    juego.Title = "SERVIDOR";
                                    juego.DataContext = this;
                                    PrepararTablero();

                                    CambiarMensaje("Seleccione la opción con la cual quiera jugar");

                                    puedeJugar = true;
                                    juego.ShowDialog();
                                    lobby.Show();

                                }));

                                break;

                            case Comando.JugadaEnviada:
                                currentDispatcher.Invoke(new Action(() =>
                                {
                                    SeleccionJugador2 = (Opcion)(long)comando.DatoJugada;
                                    int c = AsignarColumna(SeleccionJugador2);
                                    Ficha f = RevisarEspaciosBlancos(c);
                                    f.Color = (int)TipoFicha.FICHA_ROJA;
                                    CambiarMensaje("El adversario ha seleccionado su jugada.");
                                    PuedeJugar = true;
                                }));

                                _ = VerificarGanadorAsync();

                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (webSocket.State == WebSocketState.Aborted)
                {
                    juego.Close();
                    lobby.Close();
                    MainWindowVisible = true;
                    Actualizar("MainWindowVisible");
                }
                else
                    CambiarMensaje(ex.Message);
            }
        }

        private int AsignarColumna(Opcion? opcion)
        {
            if (opcion == Opcion.COL1)
            { return 0; }
            if (opcion == Opcion.COL2)
            { return 1; }
            if (opcion == Opcion.COL3)
            { return 2; }
            if (opcion == Opcion.COL4)
            { return 3; }
            if (opcion == Opcion.COL5)
            { return 4; }
            if (opcion == Opcion.COL6)
            { return 5; }
            else
            { return 6; }
        }
        public Ficha TomarFicha(int ren, int col)
        {
            if (ren >= 0 && ren < renglones && col >= 0 && col < columnas)
            {
                int x = ren * columnas + col;
                if (x >= 0 && x < ListaFichas.Count)
                    return ListaFichas[x];
            }
            return null;
        }

        public Ficha RevisarEspaciosBlancos(int columna)
        {
            Ficha f = null;
            for (int r = renglones - 1; r >= 0; r--)
            {
                if (TomarFicha(r, columna).Color == (int)TipoFicha.FICHA_VACIA)
                {
                    f = TomarFicha(r, columna);
                    break;
                }
            }
            return f;
        }
        //revisa la lista para ver si hay alguna ficha vacia, si encuentra una regresa false indicando que no es empate
        public bool Empate()
        {
            foreach (Ficha s in ListaFichas)
                if (s.Color == (int)TipoFicha.FICHA_VACIA)
                    return false;
            return true;

        }
        async Task VerificarGanadorAsync()
        {
            Ficha F1;
            Ficha F2;
            Ficha F3;
            Ficha F4;
            bool empate = Empate();
            if (empate)
            {
                PuedeJugar = false;
                CambiarMensaje("Empate, no hay ganador");
                puedeJugar = false;
                await Task.Delay(3000);
                juego.Close();
            }
            for (int row = 0; row < renglones; row++)
            {
                for (int col = 0; col < columnas; col++)
                {
                    //Horizontal
                    F1 = TomarFicha(row, col);
                    F2 = TomarFicha(row, col + 1);
                    F3 = TomarFicha(row, col + 2);
                    F4 = TomarFicha(row, col + 3);
                    bool Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_AMARILLA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador1);
                        await Task.Delay(3000);
                        juego.Close();
                    }
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_ROJA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador2);
                        await Task.Delay(3000);
                        juego.Close();
                    }

                    //Vertical
                    F1 = TomarFicha(row, col);
                    F2 = TomarFicha(row + 1, col);
                    F3 = TomarFicha(row + 2, col);
                    F4 = TomarFicha(row + 3, col);
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_AMARILLA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador1);
                        await Task.Delay(3000);
                        juego.Close();
                    }
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_ROJA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó  " + Jugador2);
                        await Task.Delay(3000);
                        juego.Close();
                    }

                    //Diagonal
                    F1 = TomarFicha(row, col);
                    F2 = TomarFicha(row + 1, col + 1);
                    F3 = TomarFicha(row + 2, col + 2);
                    F4 = TomarFicha(row + 3, col + 3);
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_AMARILLA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó  " + Jugador1);
                        await Task.Delay(3000);
                        juego.Close();
                    }
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_ROJA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador2);
                        await Task.Delay(3000);
                        juego.Close();
                    }

                    //Diagonal invertida
                    F1 = TomarFicha(row, col);
                    F2 = TomarFicha(row + 1, col - 1);
                    F3 = TomarFicha(row + 2, col - 2);
                    F4 = TomarFicha(row + 3, col - 3);
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_AMARILLA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador1);
                        await Task.Delay(3000);
                        juego.Close();
                    }
                    Conecta4 = RevisarColor(F1, F2, F3, F4, TipoFicha.FICHA_ROJA);
                    if (Conecta4)
                    {
                        PuedeJugar = false;
                        CambiarMensaje("¡Fin de juego! Ganó " + Jugador2);
                        await Task.Delay(3000);
                        juego.Close();
                    }
                }
            }
        }
        public bool RevisarColor(Ficha f1, Ficha f2, Ficha f3, Ficha f4, TipoFicha tipo)
        {
            if (f1 != null && f2 != null && f3 != null && f4 != null)
            {
                return ((int)tipo == f1.Color)
                    && ((int)tipo == f2.Color)
                    && ((int)tipo == f3.Color)
                    && ((int)tipo == f4.Color);
            }
            return false;
        }
        void CambiarMensaje(string mensaje)
        {
            currentDispatcher.Invoke(new Action(() =>
            {
                Mensaje = mensaje;
                Actualizar();
            }));
        }
        void Actualizar(string propertyName = null) //parametro con valor por defecto
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public class DatoEnviado
        {
            public Comando Comando { get; set; }
            public object DatoJugada { get; set; }
        }
    }

}