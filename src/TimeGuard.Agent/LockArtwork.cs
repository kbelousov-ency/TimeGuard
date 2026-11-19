using System.Windows;
using System.Windows.Media;

namespace TimeGuard.Agent;

// Vector artwork stays sharp at any monitor DPI and needs no external image files.
internal sealed class LockArtwork(bool night) : FrameworkElement
{
    internal static Brush B(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    internal static void Shape(DrawingContext dc, string data, Brush brush) => dc.DrawGeometry(brush, null, Geometry.Parse(data));
    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Max(ActualWidth / 1536, ActualHeight / 1024);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(new TranslateTransform((ActualWidth-1536*scale)/2, (ActualHeight-1024*scale)/2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(night ? "#06142B" : "#DAECFF"), (Color)ColorConverter.ConvertFromString(night ? "#102F58" : "#8AC6FF"), 90), null, new Rect(0,0,1536,1024));
        if (!night)
        {
            Shape(dc,"M0,470 C190,315 205,275 430,390 C700,550 890,650 1190,410 C1340,285 1450,235 1536,215 L1536,1024 L0,1024 Z", new LinearGradientBrush(Colors.LightSkyBlue, Color.FromRgb(39,116,231),90));
            Shape(dc,"M0,565 C220,375 330,755 580,710 C910,670 1060,450 1290,520 C1410,550 1450,620 1536,650 L1536,1024 L0,1024 Z",new LinearGradientBrush(Color.FromRgb(139,209,255),Color.FromRgb(29,98,223),90));
            Shape(dc,"M0,697 C260,490 410,1100 710,1050 C1080,1090 1180,460 1390,607 C1470,670 1500,730 1536,765 L1536,1024 L0,1024 Z",new LinearGradientBrush(Color.FromRgb(170,225,255),Color.FromRgb(60,137,238),75));
            Shape(dc,"M0,857 C160,920 220,1030 380,1040 L1050,1040 C1280,885 1325,760 1536,823 L1536,1024 L0,1024 Z", B("#66CDEAFF"));
        }
        else
        {
            var random = new Random(28);
            for(int i=0;i<105;i++) { double x=random.Next(1536),y=random.Next(670); dc.DrawEllipse(B(i%4==0?"#A0CFFF":"#557FAC"),null,new Point(x,y),i%4==0?1.5:.8,i%4==0?1.5:.8); }
            dc.PushTransform(new TranslateTransform(1270,150)); dc.PushTransform(new ScaleTransform(1.4,1.4));
            Shape(dc,"M42,0 C-16,12 -13,94 43,98 C65,100 77,94 86,86 C23,96 6,35 42,0 Z",new LinearGradientBrush(Color.FromRgb(172,214,255), Color.FromRgb(58,112,177),30)); dc.Pop();dc.Pop();
            Shape(dc,"M0,148 C60,110 130,155 136,215 C185,207 206,240 211,266 C250,260 275,283 287,310 L0,310 Z",B("#10284A"));
            Shape(dc,"M1338,399 C1370,371 1391,383 1405,376 C1430,347 1452,350 1465,356 C1480,321 1512,307 1536,310 L1536,399 Z",B("#132F53"));
            Shape(dc,"M0,535 C160,450 190,562 304,558 C540,650 700,592 912,660 C1170,663 1130,521 1255,533 C1420,418 1445,518 1536,538 L1536,1024 L0,1024 Z",B("#081C38"));
            for(int layer=0;layer<3;layer++)
            {
                var color=B(layer==0?"#0A2342":layer==1?"#07182F":"#041122");
                for(int i=0;i<48;i++)
                {
                    double x=i*35+random.Next(-20,20), y=700+layer*95+random.Next(-40,50), h=45+random.Next(90);
                    if(x>380 && x<1150) h*=.5;
                    Tree(dc,x,y,h,color);
                }
            }
            Shape(dc,"M0,767 C240,822 341,850 546,954 C919,1100 1215,817 1536,822 L1536,1024 L0,1024 Z",B("#051326"));
            Tree(dc,43,814,380,B("#030E1C")); Tree(dc,133,835,286,B("#041120")); Tree(dc,1504,899,330,B("#030E1C"));
            Shape(dc,"M0,875 C275,832 486,1050 772,1050 C1100,1020 1240,888 1536,948 L1536,1024 L0,1024 Z",B("#040F20"));
        }
        dc.Pop();dc.Pop();dc.Pop();
    }
    private static void Tree(DrawingContext dc,double x,double y,double h,Brush b)
    {
        dc.PushTransform(new TranslateTransform(x,y-h)); dc.PushTransform(new ScaleTransform(h/100,h/100));
        Shape(dc,"M0,0 L5,18 L12,25 L8,28 L17,39 L12,43 L23,56 L17,61 L30,78 L7,76 L7,100 L-7,100 L-7,76 L-30,78 L-17,61 L-23,56 L-12,43 L-17,39 L-8,28 L-12,25 L-5,18 Z",b);dc.Pop();dc.Pop();
    }
}
internal sealed class BrandMark : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth/132,ActualHeight/140));
        LockArtwork.Shape(dc,"M64,2 L122,27 Q124,29 124,36 L124,67 Q124,111 64,137 Q4,111 4,67 L4,36 Q4,29 8,27 Z",new LinearGradientBrush(Color.FromRgb(25,160,255),Color.FromRgb(0,91,244),90));
        dc.DrawEllipse(Brushes.White,null,new Point(64,68),38,38);
        var ink=LockArtwork.B("#112852");
        for(int i=0;i<12;i++) {double a=i*Math.PI/6; dc.DrawLine(new Pen(ink,i%3==0?3:1.5),new Point(64+30*Math.Sin(a),68-30*Math.Cos(a)),new Point(64+35*Math.Sin(a),68-35*Math.Cos(a)));}
        dc.DrawLine(new Pen(ink,6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},new Point(64,46),new Point(64,68));
        dc.DrawLine(new Pen(ink,6){EndLineCap=PenLineCap.Round},new Point(64,68),new Point(78,77));dc.DrawEllipse(ink,null,new Point(64,68),5,5);
        var shield=Geometry.Parse("M101,74 L129,85 L127,108 Q124,127 101,137 Q78,127 75,108 L73,85 Z");
        dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(15,205,133),Color.FromRgb(0,143,91),90),new Pen(Brushes.White,4),shield);
        dc.DrawGeometry(null,new Pen(Brushes.White,6),Geometry.Parse("M88,105 L97,114 L115,96")); dc.Pop();
    }
}
