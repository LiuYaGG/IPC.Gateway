#nullable enable

namespace BeckhoffAdsTool;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel root = null!;
    private GroupBox connectionGroup = null!;
    private TableLayoutPanel connectionLayout = null!;
    private Label amsNetIdLabel = null!;
    private TextBox amsNetIdTextBox = null!;
    private Label adsPortLabel = null!;
    private NumericUpDown adsPortInput = null!;
    private Label timeoutLabel = null!;
    private NumericUpDown timeoutInput = null!;
    private Button connectButton = null!;
    private GroupBox tagGroup = null!;
    private TableLayoutPanel tagLayout = null!;
    private Label symbolLabel = null!;
    private TextBox symbolTextBox = null!;
    private Label valueLabel = null!;
    private TextBox valueTextBox = null!;
    private Label dataTypeLabel = null!;
    private ComboBox dataTypeComboBox = null!;
    private Label countLabel = null!;
    private NumericUpDown countInput = null!;
    private Label helpLabel = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button readButton = null!;
    private Button writeButton = null!;
    private Button writeReadButton = null!;
    private Button clearLogButton = null!;
    private GroupBox logGroup = null!;
    private TextBox logTextBox = null!;
    private Label statusLabel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        root = new TableLayoutPanel();
        connectionGroup = new GroupBox();
        connectionLayout = new TableLayoutPanel();
        amsNetIdLabel = new Label();
        amsNetIdTextBox = new TextBox();
        adsPortLabel = new Label();
        adsPortInput = new NumericUpDown();
        timeoutLabel = new Label();
        timeoutInput = new NumericUpDown();
        connectButton = new Button();
        tagGroup = new GroupBox();
        tagLayout = new TableLayoutPanel();
        symbolLabel = new Label();
        symbolTextBox = new TextBox();
        valueLabel = new Label();
        valueTextBox = new TextBox();
        dataTypeLabel = new Label();
        dataTypeComboBox = new ComboBox();
        countLabel = new Label();
        countInput = new NumericUpDown();
        helpLabel = new Label();
        buttonPanel = new FlowLayoutPanel();
        readButton = new Button();
        writeButton = new Button();
        writeReadButton = new Button();
        clearLogButton = new Button();
        logGroup = new GroupBox();
        logTextBox = new TextBox();
        statusLabel = new Label();
        root.SuspendLayout();
        connectionGroup.SuspendLayout();
        connectionLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)adsPortInput).BeginInit();
        ((System.ComponentModel.ISupportInitialize)timeoutInput).BeginInit();
        tagGroup.SuspendLayout();
        tagLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)countInput).BeginInit();
        buttonPanel.SuspendLayout();
        logGroup.SuspendLayout();
        SuspendLayout();
        // 
        // root
        // 
        root.ColumnCount = 1;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.Controls.Add(connectionGroup, 0, 0);
        root.Controls.Add(tagGroup, 0, 1);
        root.Controls.Add(buttonPanel, 0, 2);
        root.Controls.Add(logGroup, 0, 3);
        root.Controls.Add(statusLabel, 0, 4);
        root.Dock = DockStyle.Fill;
        root.Location = new Point(0, 0);
        root.Name = "root";
        root.Padding = new Padding(16);
        root.RowCount = 5;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        root.Size = new Size(940, 680);
        root.TabIndex = 0;
        // 
        // connectionGroup
        // 
        connectionGroup.Controls.Add(connectionLayout);
        connectionGroup.Dock = DockStyle.Fill;
        connectionGroup.Location = new Point(19, 19);
        connectionGroup.Name = "connectionGroup";
        connectionGroup.Size = new Size(902, 94);
        connectionGroup.TabIndex = 0;
        connectionGroup.TabStop = false;
        connectionGroup.Text = "1. ADS 连接";
        // 
        // connectionLayout
        // 
        connectionLayout.ColumnCount = 8;
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        connectionLayout.Controls.Add(amsNetIdLabel, 0, 0);
        connectionLayout.Controls.Add(amsNetIdTextBox, 1, 0);
        connectionLayout.Controls.Add(adsPortLabel, 2, 0);
        connectionLayout.Controls.Add(adsPortInput, 3, 0);
        connectionLayout.Controls.Add(timeoutLabel, 4, 0);
        connectionLayout.Controls.Add(timeoutInput, 5, 0);
        connectionLayout.Controls.Add(connectButton, 6, 0);
        connectionLayout.Dock = DockStyle.Fill;
        connectionLayout.Location = new Point(3, 19);
        connectionLayout.Name = "connectionLayout";
        connectionLayout.Padding = new Padding(12, 8, 12, 8);
        connectionLayout.RowCount = 1;
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        connectionLayout.Size = new Size(896, 72);
        connectionLayout.TabIndex = 0;
        // 
        // amsNetIdLabel
        // 
        amsNetIdLabel.Anchor = AnchorStyles.Right;
        amsNetIdLabel.AutoSize = true;
        amsNetIdLabel.Location = new Point(22, 33);
        amsNetIdLabel.Margin = new Padding(3, 12, 3, 3);
        amsNetIdLabel.Name = "amsNetIdLabel";
        amsNetIdLabel.Size = new Size(77, 15);
        amsNetIdLabel.TabIndex = 0;
        amsNetIdLabel.Text = "AMS NetId：";
        // 
        // amsNetIdTextBox
        // 
        amsNetIdTextBox.Dock = DockStyle.Fill;
        amsNetIdTextBox.Location = new Point(105, 16);
        amsNetIdTextBox.Margin = new Padding(3, 8, 3, 3);
        amsNetIdTextBox.Name = "amsNetIdTextBox";
        amsNetIdTextBox.Size = new Size(184, 23);
        amsNetIdTextBox.TabIndex = 1;
        amsNetIdTextBox.Text = "127.0.0.1.1.1";
        // 
        // adsPortLabel
        // 
        adsPortLabel.Anchor = AnchorStyles.Right;
        adsPortLabel.AutoSize = true;
        adsPortLabel.Location = new Point(314, 25);
        adsPortLabel.Margin = new Padding(3, 12, 3, 3);
        adsPortLabel.Name = "adsPortLabel";
        adsPortLabel.Size = new Size(45, 30);
        adsPortLabel.TabIndex = 2;
        adsPortLabel.Text = "ADS 端口：";
        // 
        // adsPortInput
        // 
        adsPortInput.Dock = DockStyle.Fill;
        adsPortInput.Location = new Point(365, 15);
        adsPortInput.Margin = new Padding(3, 7, 3, 3);
        adsPortInput.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        adsPortInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        adsPortInput.Name = "adsPortInput";
        adsPortInput.Size = new Size(79, 23);
        adsPortInput.TabIndex = 3;
        adsPortInput.Value = new decimal(new int[] { 851, 0, 0, 0 });
        // 
        // timeoutLabel
        // 
        timeoutLabel.Anchor = AnchorStyles.Right;
        timeoutLabel.AutoSize = true;
        timeoutLabel.Location = new Point(459, 33);
        timeoutLabel.Margin = new Padding(3, 12, 3, 3);
        timeoutLabel.Name = "timeoutLabel";
        timeoutLabel.Size = new Size(70, 15);
        timeoutLabel.TabIndex = 4;
        timeoutLabel.Text = "超时(ms)：";
        // 
        // timeoutInput
        // 
        timeoutInput.Dock = DockStyle.Fill;
        timeoutInput.Increment = new decimal(new int[] { 500, 0, 0, 0 });
        timeoutInput.Location = new Point(535, 15);
        timeoutInput.Margin = new Padding(3, 7, 3, 3);
        timeoutInput.Maximum = new decimal(new int[] { 60000, 0, 0, 0 });
        timeoutInput.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
        timeoutInput.Name = "timeoutInput";
        timeoutInput.Size = new Size(84, 23);
        timeoutInput.TabIndex = 5;
        timeoutInput.Value = new decimal(new int[] { 5000, 0, 0, 0 });
        // 
        // connectButton
        // 
        connectButton.AutoSize = true;
        connectButton.BackColor = Color.White;
        connectButton.Location = new Point(626, 11);
        connectButton.Margin = new Padding(4, 3, 4, 3);
        connectButton.MinimumSize = new Size(96, 34);
        connectButton.Name = "connectButton";
        connectButton.Padding = new Padding(8, 2, 8, 2);
        connectButton.Size = new Size(96, 34);
        connectButton.TabIndex = 6;
        connectButton.Text = "连接";
        connectButton.UseVisualStyleBackColor = true;
        // 
        // tagGroup
        // 
        tagGroup.Controls.Add(tagLayout);
        tagGroup.Dock = DockStyle.Fill;
        tagGroup.Location = new Point(19, 119);
        tagGroup.Name = "tagGroup";
        tagGroup.Size = new Size(902, 184);
        tagGroup.TabIndex = 1;
        tagGroup.TabStop = false;
        tagGroup.Text = "2. 点位读写";
        // 
        // tagLayout
        // 
        tagLayout.ColumnCount = 6;
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        tagLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        tagLayout.Controls.Add(symbolLabel, 0, 0);
        tagLayout.Controls.Add(symbolTextBox, 1, 0);
        tagLayout.Controls.Add(valueLabel, 0, 1);
        tagLayout.Controls.Add(valueTextBox, 1, 1);
        tagLayout.Controls.Add(dataTypeLabel, 2, 1);
        tagLayout.Controls.Add(dataTypeComboBox, 3, 1);
        tagLayout.Controls.Add(countLabel, 4, 1);
        tagLayout.Controls.Add(countInput, 5, 1);
        tagLayout.Controls.Add(helpLabel, 1, 2);
        tagLayout.Dock = DockStyle.Fill;
        tagLayout.Location = new Point(3, 19);
        tagLayout.Name = "tagLayout";
        tagLayout.Padding = new Padding(12, 8, 12, 8);
        tagLayout.RowCount = 3;
        tagLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        tagLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        tagLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tagLayout.Size = new Size(896, 162);
        tagLayout.TabIndex = 0;
        // 
        // symbolLabel
        // 
        symbolLabel.Anchor = AnchorStyles.Right;
        symbolLabel.AutoSize = true;
        symbolLabel.Location = new Point(22, 27);
        symbolLabel.Margin = new Padding(3, 12, 3, 3);
        symbolLabel.Name = "symbolLabel";
        symbolLabel.Size = new Size(72, 15);
        symbolLabel.TabIndex = 0;
        symbolLabel.Text = "符号地址：";
        // 
        // symbolTextBox
        // 
        tagLayout.SetColumnSpan(symbolTextBox, 5);
        symbolTextBox.Dock = DockStyle.Fill;
        symbolTextBox.Location = new Point(100, 16);
        symbolTextBox.Margin = new Padding(3, 8, 3, 3);
        symbolTextBox.Name = "symbolTextBox";
        symbolTextBox.Size = new Size(781, 23);
        symbolTextBox.TabIndex = 1;
        symbolTextBox.Text = "GVL.iCounter";
        // 
        // valueLabel
        // 
        valueLabel.Anchor = AnchorStyles.Right;
        valueLabel.AutoSize = true;
        valueLabel.Location = new Point(61, 71);
        valueLabel.Margin = new Padding(3, 12, 3, 3);
        valueLabel.Name = "valueLabel";
        valueLabel.Size = new Size(33, 15);
        valueLabel.TabIndex = 2;
        valueLabel.Text = "值：";
        // 
        // valueTextBox
        // 
        valueTextBox.Dock = DockStyle.Fill;
        valueTextBox.Location = new Point(100, 60);
        valueTextBox.Margin = new Padding(3, 8, 3, 3);
        valueTextBox.Name = "valueTextBox";
        valueTextBox.Size = new Size(331, 23);
        valueTextBox.TabIndex = 3;
        valueTextBox.Text = "123";
        // 
        // dataTypeLabel
        // 
        dataTypeLabel.Anchor = AnchorStyles.Right;
        dataTypeLabel.AutoSize = true;
        dataTypeLabel.Location = new Point(439, 71);
        dataTypeLabel.Margin = new Padding(3, 12, 3, 3);
        dataTypeLabel.Name = "dataTypeLabel";
        dataTypeLabel.Size = new Size(72, 15);
        dataTypeLabel.TabIndex = 4;
        dataTypeLabel.Text = "数据类型：";
        // 
        // dataTypeComboBox
        // 
        dataTypeComboBox.Dock = DockStyle.Fill;
        dataTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dataTypeComboBox.Location = new Point(517, 58);
        dataTypeComboBox.Margin = new Padding(3, 6, 3, 3);
        dataTypeComboBox.Name = "dataTypeComboBox";
        dataTypeComboBox.Size = new Size(144, 23);
        dataTypeComboBox.TabIndex = 5;
        // 
        // countLabel
        // 
        countLabel.Anchor = AnchorStyles.Right;
        countLabel.AutoSize = true;
        countLabel.Location = new Point(684, 71);
        countLabel.Margin = new Padding(3, 12, 3, 3);
        countLabel.Name = "countLabel";
        countLabel.Size = new Size(77, 15);
        countLabel.TabIndex = 6;
        countLabel.Text = "数量/长度：";
        // 
        // countInput
        // 
        countInput.Dock = DockStyle.Fill;
        countInput.Location = new Point(767, 59);
        countInput.Margin = new Padding(3, 7, 3, 3);
        countInput.Maximum = new decimal(new int[] { 4096, 0, 0, 0 });
        countInput.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        countInput.Name = "countInput";
        countInput.Size = new Size(114, 23);
        countInput.TabIndex = 7;
        countInput.Value = new decimal(new int[] { 1, 0, 0, 0 });
        // 
        // helpLabel
        // 
        helpLabel.AutoSize = true;
        tagLayout.SetColumnSpan(helpLabel, 5);
        helpLabel.ForeColor = Color.DimGray;
        helpLabel.Location = new Point(100, 104);
        helpLabel.Margin = new Padding(3, 8, 3, 3);
        helpLabel.Name = "helpLabel";
        helpLabel.Size = new Size(541, 15);
        helpLabel.TabIndex = 8;
        helpLabel.Text = "数组用逗号分隔；读取数组时“数量”为元素数；STRING 时“长度”为 PLC 声明长度（默认 80）。";
        // 
        // buttonPanel
        // 
        buttonPanel.Controls.Add(readButton);
        buttonPanel.Controls.Add(writeButton);
        buttonPanel.Controls.Add(writeReadButton);
        buttonPanel.Controls.Add(clearLogButton);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.Location = new Point(19, 309);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Padding = new Padding(0, 8, 0, 0);
        buttonPanel.Size = new Size(902, 48);
        buttonPanel.TabIndex = 2;
        buttonPanel.WrapContents = false;
        // 
        // readButton
        // 
        readButton.AutoSize = true;
        readButton.BackColor = Color.White;
        readButton.Location = new Point(4, 11);
        readButton.Margin = new Padding(4, 3, 4, 3);
        readButton.MinimumSize = new Size(96, 34);
        readButton.Name = "readButton";
        readButton.Padding = new Padding(8, 2, 8, 2);
        readButton.Size = new Size(96, 34);
        readButton.TabIndex = 0;
        readButton.Text = "读取";
        readButton.UseVisualStyleBackColor = true;
        // 
        // writeButton
        // 
        writeButton.AutoSize = true;
        writeButton.BackColor = Color.White;
        writeButton.Location = new Point(108, 11);
        writeButton.Margin = new Padding(4, 3, 4, 3);
        writeButton.MinimumSize = new Size(96, 34);
        writeButton.Name = "writeButton";
        writeButton.Padding = new Padding(8, 2, 8, 2);
        writeButton.Size = new Size(96, 34);
        writeButton.TabIndex = 1;
        writeButton.Text = "写入";
        writeButton.UseVisualStyleBackColor = true;
        // 
        // writeReadButton
        // 
        writeReadButton.AutoSize = true;
        writeReadButton.BackColor = Color.White;
        writeReadButton.Location = new Point(212, 11);
        writeReadButton.Margin = new Padding(4, 3, 4, 3);
        writeReadButton.MinimumSize = new Size(96, 34);
        writeReadButton.Name = "writeReadButton";
        writeReadButton.Padding = new Padding(8, 2, 8, 2);
        writeReadButton.Size = new Size(98, 34);
        writeReadButton.TabIndex = 2;
        writeReadButton.Text = "写入并读回";
        writeReadButton.UseVisualStyleBackColor = true;
        // 
        // clearLogButton
        // 
        clearLogButton.AutoSize = true;
        clearLogButton.BackColor = Color.White;
        clearLogButton.Location = new Point(318, 11);
        clearLogButton.Margin = new Padding(4, 3, 4, 3);
        clearLogButton.MinimumSize = new Size(96, 34);
        clearLogButton.Name = "clearLogButton";
        clearLogButton.Padding = new Padding(8, 2, 8, 2);
        clearLogButton.Size = new Size(96, 34);
        clearLogButton.TabIndex = 3;
        clearLogButton.Text = "清空日志";
        clearLogButton.UseVisualStyleBackColor = true;
        // 
        // logGroup
        // 
        logGroup.Controls.Add(logTextBox);
        logGroup.Dock = DockStyle.Fill;
        logGroup.Location = new Point(19, 363);
        logGroup.Name = "logGroup";
        logGroup.Size = new Size(902, 268);
        logGroup.TabIndex = 3;
        logGroup.TabStop = false;
        logGroup.Text = "运行日志";
        // 
        // logTextBox
        // 
        logTextBox.BackColor = Color.White;
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Font = new Font("Consolas", 9F);
        logTextBox.Location = new Point(3, 19);
        logTextBox.Multiline = true;
        logTextBox.Name = "logTextBox";
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.Size = new Size(896, 246);
        logTextBox.TabIndex = 0;
        // 
        // statusLabel
        // 
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.DimGray;
        statusLabel.Location = new Point(19, 634);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(902, 30);
        statusLabel.TabIndex = 4;
        statusLabel.Text = "未连接";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(246, 248, 251);
        ClientSize = new Size(940, 680);
        Controls.Add(root);
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(840, 600);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "倍福 ADS 读写客户端";
        root.ResumeLayout(false);
        connectionGroup.ResumeLayout(false);
        connectionLayout.ResumeLayout(false);
        connectionLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)adsPortInput).EndInit();
        ((System.ComponentModel.ISupportInitialize)timeoutInput).EndInit();
        tagGroup.ResumeLayout(false);
        tagLayout.ResumeLayout(false);
        tagLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)countInput).EndInit();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        logGroup.ResumeLayout(false);
        logGroup.PerformLayout();
        ResumeLayout(false);
    }
}
