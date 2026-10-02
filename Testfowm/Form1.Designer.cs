#nullable enable
namespace Testfowm;

partial class Form1
{
    private System.ComponentModel.IContainer? components;
    private ComboBox cameraSelector = null!;
    private Button refreshButton = null!;
    private Button startButton = null!;
    private Button centerButton = null!;
    private PictureBox myPictureBox = null!;
    private Label statusLabel = null!;
    private Label targetLabel = null!;
    private Label cameraLabel = null!;
    private Label confidenceLabel = null!;
    private NumericUpDown confidenceInput = null!;
    private RobotControlPanel robotPanel = null!;
    private TableLayoutPanel layout = null!;
    private FlowLayoutPanel toolbar = null!;
    private FlowLayoutPanel information = null!;
    private System.Windows.Forms.Timer previewTimer = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            myPictureBox?.Image?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        cameraSelector = new ComboBox();
        refreshButton = new Button();
        startButton = new Button();
        centerButton = new Button();
        myPictureBox = new PictureBox();
        statusLabel = new Label();
        targetLabel = new Label();
        cameraLabel = new Label();
        confidenceLabel = new Label();
        confidenceInput = new NumericUpDown();
        robotPanel = new RobotControlPanel();
        layout = new TableLayoutPanel();
        toolbar = new FlowLayoutPanel();
        information = new FlowLayoutPanel();
        previewTimer = new System.Windows.Forms.Timer(components);
        ((System.ComponentModel.ISupportInitialize)myPictureBox).BeginInit();
        ((System.ComponentModel.ISupportInitialize)confidenceInput).BeginInit();
        layout.SuspendLayout();
        toolbar.SuspendLayout();
        information.SuspendLayout();
        SuspendLayout();

        cameraSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        cameraSelector.Size = new Size(260, 23);
        cameraSelector.Name = "cameraSelector";
        refreshButton.Text = "Обновить камеры";
        refreshButton.AutoSize = true;
        refreshButton.Name = "refreshButton";
        refreshButton.Click += RefreshCameras_Click;
        startButton.Text = "Запустить камеру";
        startButton.AutoSize = true;
        startButton.Name = "startButton";
        startButton.Click += StartButton_Click;
        centerButton.Text = "Прицел в центр";
        centerButton.AutoSize = true;
        centerButton.Name = "centerButton";
        centerButton.Click += CenterButton_Click;
        cameraLabel.Text = "Камера:";
        cameraLabel.AutoSize = true;
        cameraLabel.Margin = new Padding(3, 7, 6, 3);
        cameraLabel.Name = "cameraLabel";
        confidenceLabel.Text = "Порог, %:";
        confidenceLabel.AutoSize = true;
        confidenceLabel.Margin = new Padding(8, 7, 3, 3);
        confidenceLabel.Name = "confidenceLabel";
        confidenceInput.Minimum = 20;
        confidenceInput.Maximum = 95;
        confidenceInput.Value = 40;
        confidenceInput.Width = 55;
        confidenceInput.Name = "confidenceInput";
        confidenceInput.ValueChanged += Confidence_ValueChanged;

        myPictureBox.Dock = DockStyle.Fill;
        myPictureBox.BackColor = Color.FromArgb(24, 24, 24);
        myPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        myPictureBox.Name = "myPictureBox";
        myPictureBox.MouseClick += Preview_MouseClick;
        statusLabel.AutoSize = true;
        statusLabel.Text = "Выбери камеру и нажми «Запустить камеру».";
        statusLabel.Name = "statusLabel";
        targetLabel.AutoSize = true;
        targetLabel.Text = "Нажми на видео, чтобы поставить прицел. Автослежение выключено.";
        targetLabel.Name = "targetLabel";

        toolbar.Dock = DockStyle.Fill;
        toolbar.AutoSize = true;
        toolbar.WrapContents = true;
        toolbar.Padding = new Padding(8);
        toolbar.Name = "toolbar";
        toolbar.Controls.Add(cameraLabel);
        toolbar.Controls.Add(cameraSelector);
        toolbar.Controls.Add(refreshButton);
        toolbar.Controls.Add(startButton);
        toolbar.Controls.Add(centerButton);
        toolbar.Controls.Add(confidenceLabel);
        toolbar.Controls.Add(confidenceInput);
        information.Dock = DockStyle.Fill;
        information.AutoSize = true;
        information.FlowDirection = FlowDirection.TopDown;
        information.WrapContents = false;
        information.Padding = new Padding(8);
        information.Name = "information";
        information.Controls.Add(statusLabel);
        information.Controls.Add(targetLabel);
        layout.Dock = DockStyle.Fill;
        layout.ColumnCount = 1;
        layout.RowCount = 4;
        layout.Name = "layout";
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(myPictureBox, 0, 1);
        layout.Controls.Add(information, 0, 2);
        layout.Controls.Add(robotPanel, 0, 3);
        previewTimer.Interval = 33;
        previewTimer.Tick += PreviewTimer_Tick;

        AutoScaleDimensions = new SizeF(7, 15);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 870);
        MinimumSize = new Size(760, 500);
        Name = "Form1";
        Text = "AiSensor — наблюдение и калибровка";
        Controls.Add(layout);
        ((System.ComponentModel.ISupportInitialize)myPictureBox).EndInit();
        ((System.ComponentModel.ISupportInitialize)confidenceInput).EndInit();
        toolbar.ResumeLayout(false);
        toolbar.PerformLayout();
        information.ResumeLayout(false);
        information.PerformLayout();
        layout.ResumeLayout(false);
        layout.PerformLayout();
        ResumeLayout(false);
    }
}
