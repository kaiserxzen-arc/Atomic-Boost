namespace Atomic.Core;

/// <summary>Thai names and descriptions for the tweaks in Catalog, by tweak id. A tweak without an entry is shown in English.</summary>
public static class CatalogTh
{
    public static void Fill(List<Tweak> tweaks, SystemInfo pc)
    {
        bool laptop = pc.IsLaptop;
        string gpu = pc.Gpu?.Name ?? "การ์ดจอ";
        string msiGpu = pc.Gpus.FirstOrDefault(g => g.Vendor is "NVIDIA" or "AMD" && g.Pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))?.Name ?? gpu;
        string ac = laptop ? " บนแล็ปท็อปเครื่องนี้จะมีผลเฉพาะตอนเสียบปลั๊ก ไม่กระทบอายุแบตเตอรี่" : "";
        double ramGb = pc.RamBytes / 1073741824d;
        bool plentyOfRam = ramGb >= 24;

        var th = new Dictionary<string, (string Name, string Desc)>
        {
            // ------------------------------------------------------------ POWER & CPU
            ["pwr_plan"] = ("แผนพลังงาน Atomic Ultimate Performance",
                "สร้างและเปิดใช้แผนพลังงาน Ultimate Performance โดยเฉพาะ เพื่อให้ CPU ไม่ต้องรอเร่งความเร็ว ควรใช้ตัวนี้ก่อน เพื่อให้การปรับแต่งด้านพลังงานด้านล่างมีผลกับแผนนี้"
                + (laptop ? " ไม่แนะนำบนแล็ปท็อป เพราะมีผลตอนใช้แบตเตอรี่ด้วยและทำให้เครื่องร้อนขึ้น ควรใช้ Best Performance Power Mode แทน" : "")),
            ["lap_powermode"] = ("โหมดพลังงาน Best Performance",
                "ตั้งแถบเลื่อนโหมดพลังงานของ Windows เป็น Best Performance เพื่อปลดขีดจำกัดกำลัง CPU และ GPU ที่แล็ปท็อปใช้ในโหมด Balanced เป็นจุดที่ได้ผลมากที่สุดบนแล็ปท็อป ควรเล่นตอนเสียบปลั๊ก"),
            ["pwr_coreparking"] = ("ปิด CPU Core Parking",
                $"ให้ทั้ง {Math.Max(pc.CpuCores, 1)} คอร์ตื่นอยู่ตลอด คอร์ที่ถูกพักแล้วต้องตื่นกลางเฟรมเป็นสาเหตุของอาการกระตุกเล็ก ๆ (micro-stutter){ac}"),
            ["pwr_minstate"] = ("Minimum Processor State 100%",
                "ไม่ให้ Windows ลดความเร็ว CPU ระหว่างเฟรม ช่วยให้ frame time นิ่งขึ้นในเกมที่ใช้ CPU หนัก"
                + (laptop ? " เป็นตัวเลือกเสริมบนแล็ปท็อป เพราะทำให้อุณหภูมิและเสียงพัดลมตอนเครื่องว่างสูงขึ้นขณะเสียบปลั๊ก" : "")),
            ["pwr_boost"] = ("Turbo Boost แบบ Aggressive",
                $"ตั้งโหมด boost ของโปรเซสเซอร์เป็น Aggressive เพื่อให้ CPU กระโดดไปที่ความถี่ turbo ทันทีแทนการค่อย ๆ เร่ง{ac}"),
            ["pwr_epp"] = ("Energy Preference เน้นประสิทธิภาพ (EPP)",
                $"บอก CPU รุ่นใหม่ (Intel Speed Shift, AMD CPPC) ให้เลือกความเร็วมากกว่าการประหยัดพลังงานเมื่อเลือกความถี่ ความเร็วจึงขึ้นไวกว่าเมื่อมีโหลด{ac}"),
            ["pwr_cooling"] = ("นโยบายระบายความร้อนแบบ Active",
                $"ให้ Windows เร่งพัดลมก่อนที่จะลดความเร็วโปรเซสเซอร์เมื่อเครื่องร้อน แทนการลดความเร็วก่อน{ac}"),
            ["pwr_usb"] = ("ปิด USB Selective Suspend",
                $"ไม่ให้ Windows สั่งพอร์ต USB เข้าโหมดพัก ป้องกันเมาส์ คีย์บอร์ด และหูฟังหลุด หรือหน่วงตอนเริ่มใช้หลังปล่อยทิ้งไว้{ac}"),
            ["pwr_aspm"] = ("ปิดการประหยัดพลังงาน PCIe Link State",
                $"ให้ลิงก์ PCIe ไปยัง {gpu} และไดรฟ์ NVMe ทำงานเต็มกำลัง สถานะประหยัดพลังงานของลิงก์เพิ่มความหน่วงตอนปลุกทุกครั้งที่อุปกรณ์ว่างชั่วครู่{ac}"),
            ["pwr_diskidle"] = ("ไม่หยุดหมุนฮาร์ดดิสก์",
                pc.HasHDD ? "ตรวจพบฮาร์ดดิสก์ในเครื่อง เมื่อ HDD ที่หลับอยู่ต้องหมุนขึ้นใหม่ เกมจะค้าง 2-5 วินาทีตอนโหลดข้อมูล ตัวนี้ทำให้ไดรฟ์หมุนอยู่ตลอด"
                          : "ไม่ให้ Windows หยุดหมุนฮาร์ดดิสก์ เครื่องนี้ไม่พบฮาร์ดดิสก์ จึงเป็นตัวเลือกเสริม"),
            ["pwr_hibernate"] = ("ปิด Hibernation และ Fast Startup",
                $"ลบไฟล์ hiberfil.sys (ประมาณ {Math.Max(1, ramGb * 0.4):N0} GB บนเครื่องนี้) และปิด Fast Startup ซึ่งเก็บไดรเวอร์ไว้ในสถานะกึ่งบันทึกระหว่างการบูต และเป็นสาเหตุที่พบบ่อยของอาการกระตุกและไดรเวอร์รวน"
                + (laptop ? " ไม่แนะนำบนแล็ปท็อป เพราะ hibernation ช่วยรักษางานของคุณเมื่อแบตเตอรี่หมด" : "")),
            ["fps_powerthrottle"] = ("ปิด Power Throttling ของ Windows",
                "Power Throttling ลดความเร็วโปรเซสเบื้องหลังเพื่อประหยัดพลังงาน แต่บางครั้ง Windows จัดตัวช่วยของเกม โปรแกรมคุยเสียง หรือ overlay เป็นงานเบื้องหลังไปด้วย ตัวนี้ปิดทั้งระบบ"
                + (laptop ? " เป็นตัวเลือกเสริมบนแล็ปท็อป เพราะทำให้แบตเตอรี่หมดเร็วขึ้น" : "")),

            // ------------------------------------------------------------ FPS & LATENCY
            ["fps_gamemode"] = ("เปิด Game Mode ของ Windows",
                "Game Mode ไม่ให้ Windows Update ติดตั้งไดรเวอร์หรือขึ้นข้อความให้รีสตาร์ตระหว่างเล่น และให้เกมมีความสำคัญเหนือกว่างานเบื้องหลัง"),
            ["fps_gamedvr"] = ("ปิดการอัดวิดีโอเบื้องหลังของ Game DVR",
                "การอัดเบื้องหลังเข้ารหัสวิดีโอการเล่นของคุณตลอดเวลา ซึ่งกิน FPS จริง โดยเฉพาะบน GPU ระดับเริ่มต้นและ CPU ที่มีคอร์น้อย"),
            ["fps_gamebar"] = ("ปิด Overlay ของ Xbox Game Bar",
                "ไม่ให้ overlay Win+G เข้าไปเกาะในทุกเกม ประหยัด RAM และลดชั้น overlay ลงหนึ่งชั้น แต่จะใช้ widget ของ Game Bar ไม่ได้ (ใช้ overlay ของค่ายการ์ดจอหรือ Steam แทนได้)"),
            ["fps_hpet"] = ("ไม่บังคับใช้ HPET",
                "โปรแกรม \"optimizer\" บางตัวบังคับให้ใช้ตัวจับเวลา HPET ที่ช้าเป็นแหล่งเวลาเดียว ทำให้ทุกการเรียกตัวจับเวลามี overhead ตัวนี้ลบการบังคับนั้นออก เพื่อให้ Windows ใช้ TSC ที่เร็วกว่า"),
            ["fps_dyntick"] = ("ปิด Dynamic Tick",
                "ให้ตัวจับเวลาระบบเดินด้วยอัตราคงที่แทนการหยุดเพื่อประหยัดพลังงาน อาจช่วยให้การตอบสนองและจังหวะเฟรมสม่ำเสมอขึ้นเล็กน้อย"
                + (laptop ? " ไม่ควรใช้บนแล็ปท็อป เพราะทำให้เครื่องเข้าสถานะพักลึกไม่ได้และเปลืองแบตเตอรี่" : " ทำให้กินไฟตอนเครื่องว่างเพิ่มขึ้นเล็กน้อย")),
            ["fps_fso"] = ("ปิด Fullscreen Optimizations (ทั้งระบบ)",
                "บังคับใช้ exclusive fullscreen แบบดั้งเดิมในเกม DirectX 9/11 รุ่นเก่า ซึ่งอาจลด input latency ได้ บน Windows 11 ค่าเริ่มต้น (flip model) มักดีพอ ๆ กัน ควรลองกับเกมของคุณก่อน"),
            ["fps_mouse"] = ("ปิด Mouse Acceleration",
                "ปิด \"Enhance pointer precision\" เพื่อให้เมาส์เคลื่อนที่แบบ 1:1 การเล็งจะสม่ำเสมอ เพราะขยับมือเท่าเดิมจะหมุนได้ระยะเท่าเดิมเสมอ"),
            ["fps_keyboard"] = ("คีย์บอร์ดตอบสนองการกดค้างเร็วที่สุด",
                "ตั้งค่าหน่วงก่อนกดซ้ำให้สั้นที่สุดและอัตรากดซ้ำเร็วที่สุด ปุ่มที่กดค้างจึงตอบสนองทันที"),
            ["fps_bgapps"] = ("หยุดแอปเบื้องหลัง",
                $"ไม่ให้แอป Store (Weather, Mail, Phone Link ฯลฯ) ทำงานเบื้องหลังและแย่งเวลา CPU จาก {pc.CpuThreads} เธรดของคุณ แอปบางตัวจะไม่ส่งการแจ้งเตือนแบบสดอีก"),
            ["fps_edge"] = ("หยุด Startup Boost และโหมดเบื้องหลังของ Edge",
                "Microsoft Edge โหลดตัวเองล่วงหน้าตอนบูตและทำงานต่อหลังปิดหน้าต่าง ตัวนี้หยุดทั้งสองอย่าง ช่วยคืน RAM และ CPU (Edge จะแสดงข้อความ \"managed by your organization\" ซึ่งเป็นปกติของ policy นี้)"),
            ["fps_noreboot"] = ("ไม่บังคับรีสตาร์ตจากอัปเดตขณะล็อกอินอยู่",
                "ไม่ให้ Windows Update รีสตาร์ตเครื่องเองขณะที่คุณล็อกอินอยู่ ไม่มีการรีสตาร์ตกะทันหันกลางแมตช์อีก"),

            // ------------------------------------------------------------ GPU & DISPLAY
            ["gpu_hags"] = ("Hardware-Accelerated GPU Scheduling",
                $"ให้ {gpu} จัดการคิวหน่วยความจำวิดีโอเองแทน CPU ช่วยคืนเวลา CPU และอาจลดความหน่วง อีกทั้งจำเป็นสำหรับ frame generation ต้องใช้ไดรเวอร์ GPU รุ่นใหม่"),
            ["gpu_mpo"] = ("ปิด Multiplane Overlay (MPO)",
                "MPO เป็นสาเหตุที่รู้จักกันของอาการภาพกะพริบ จอดำ และกระตุกในเกมโหมด borderless/หน้าต่าง โดยเฉพาะบนการ์ด NVIDIA ซึ่ง NVIDIA เองก็เผยแพร่วิธีแก้นี้ ย้อนกลับได้อย่างปลอดภัย"),
            ["gpu_winopt"] = ("Optimizations สำหรับเกมโหมดหน้าต่าง",
                "ยกระดับเกม DirectX 10/11 รุ่นเก่าที่รันแบบ borderless หรือหน้าต่างให้ใช้ flip presentation model รุ่นใหม่ ความหน่วงต่ำลงจนใกล้เคียง exclusive fullscreen"),
            ["gpu_msi"] = ("เปิด MSI Mode ให้ GPU",
                $"เปลี่ยน {msiGpu} จาก interrupt แบบ line-based รุ่นเก่าเป็น Message Signaled Interrupts ซึ่งลดความหน่วงของ interrupt และเลี่ยงการใช้ IRQ ร่วมกัน ต้องใช้ซ้ำหลังติดตั้งไดรเวอร์แบบ clean install"),
            ["gpu_transparency"] = ("ปิดเอฟเฟกต์โปร่งใส",
                "เอาเอฟเฟกต์เบลอ/acrylic ออกจาก taskbar, Start และหน้าต่าง ลดงานของ Desktop Window Manager โดยเฉพาะในเกมโหมด borderless และบนการ์ดจอออนบอร์ด"),
            ["gpu_visualfx"] = ("Visual Effects: เน้นประสิทธิภาพ",
                "ปิดแอนิเมชันหน้าต่าง การเฟด เงา และ Aero Peek โดยยังคงฟอนต์เรียบและภาพ thumbnail ไว้ Windows จะรู้สึกไวขึ้นและ GPU ทำงานกับเดสก์ท็อปน้อยลง ส่วนสวิตช์ \"Animation effects\" ของ Windows คงไว้ตามเดิม เบราว์เซอร์และแอปอื่นจึงยังมีแอนิเมชันของตัวเอง"),
            ["gpu_nvtelemetry"] = ("ปิด Telemetry ของ NVIDIA",
                "หยุดบริการ telemetry ของ NVIDIA และ scheduled task ที่ส่งรายงาน ไดรเวอร์ control panel และโปรไฟล์เกมยังทำงานตามปกติ"),
            ["gpu_wudrivers"] = ("ไม่ให้ Windows Update เปลี่ยนไดรเวอร์",
                "ป้องกัน Windows Update แอบเปลี่ยนไดรเวอร์ GPU เป็นรุ่นทั่วไปที่เก่ากว่า (สาเหตุที่พบบ่อยของ FPS ตกกะทันหัน) ให้อัปเดตไดรเวอร์จากผู้ผลิตแทน"),

            // ------------------------------------------------------------ REGISTRY
            ["reg_mmcss_games"] = ("ให้ความสำคัญกับเกมใน Multimedia Scheduler",
                "ยกโปรไฟล์งาน \"Games\" ของ MMCSS เป็น scheduling category ระดับ High, CPU priority 6 และ GPU priority 8 เธรดของเกมที่ลงทะเบียนกับ MMCSS จึงได้สิทธิ์เหนืองานเบื้องหลัง"),
            ["reg_sysresp"] = ("System Responsiveness สำหรับเล่นเกม",
                "ลดสัดส่วน CPU ที่ Windows กันไว้ให้งานเบื้องหลังระหว่างเล่นมัลติมีเดีย/เกม จาก 20% เหลือขั้นต่ำ 10% เพื่อให้เกมได้ CPU มากขึ้น"),
            ["reg_priosep"] = ("เพิ่ม Priority ให้หน้าต่างที่ใช้งานอยู่ (Win32PrioritySeparation)",
                "ตั้งช่วงเวลา CPU แบบสั้นและแปรผัน พร้อม boost สูงสุดให้หน้าต่างที่โฟกัสอยู่ (ค่า 0x26) เกมที่กำลังเล่นจึงได้ CPU ก่อน"),
            ["reg_timerres"] = ("Global Timer Resolution Requests",
                "Windows 11 ไม่สนใจคำขอ timer ความละเอียดสูงจากโปรเซสที่ย่อหน้าต่างหรืออยู่เบื้องหลัง ตัวนี้คืนพฤติกรรมแบบ global ดั้งเดิม เพื่อให้เกมและตัวจำกัดเฟรมได้ timer แม่นยำระดับ 0.5-1 ms"
                + (laptop ? " เป็นตัวเลือกเสริมบนแล็ปท็อป เพราะ timer ที่ถูกยกระดับตลอดเวลาทำให้เปลืองแบตเตอรี่" : "")),
            ["reg_pagingexec"] = ("เก็บ Kernel และไดรเวอร์ไว้ใน RAM",
                $"ด้วย RAM {ramGb:N0} GB {(ramGb >= 15 ? "ไม่มีเหตุผลที่จะย้ายโค้ด kernel และไดรเวอร์ไปไว้บนดิสก์" : "ตัวนี้เป็นตัวเลือกเสริม เพราะใช้หน่วยความจำเพิ่มขึ้นเล็กน้อย")} การตั้ง DisablePagingExecutive จะเก็บไว้ในหน่วยความจำ ซึ่งช่วยได้มากที่สุดเมื่อ Windows อยู่บนฮาร์ดดิสก์"),
            ["reg_svchost"] = ("รวมกลุ่มโปรเซส Service Host",
                "ตั้ง SvcHostSplitThresholdInKB เท่ากับ RAM ที่ติดตั้ง เพื่อให้ Windows รวมบริการไว้ใน svchost.exe จำนวนน้อยลง ลดโปรเซสได้หลายสิบตัวและลดภาระของ scheduler"),
            ["reg_menudelay"] = ("เมนูเปิดทันที",
                "เอาหน่วงเวลา 400 ms ก่อนเมนูย่อยเปิดออก (MenuShowDelay = 0) เดสก์ท็อปจะตอบสนองทันที"),
            ["reg_startupdelay"] = ("เอาหน่วงเวลาแอปเริ่มต้นออก",
                "Windows จงใจรอก่อนเปิดแอปเริ่มต้น ตัวนี้เอาหน่วงเวลานั้นออก เพื่อให้เดสก์ท็อปพร้อมใช้เร็วขึ้นหลังบูต"),
            ["reg_shutdown"] = ("ปิดเครื่องเร็วขึ้นและตรวจแอปค้างไวขึ้น",
                "ลดเวลาที่ Windows รอแอปและบริการที่ค้างตอนปิดเครื่องหรือรีสตาร์ต แอปยังมีโอกาสบันทึกงานตามปกติ"),

            // ------------------------------------------------------------ MEMORY & STORAGE
            ["mem_compression"] = ("ปิด Memory Compression",
                plentyOfRam ? $"Memory compression แลกเวลา CPU กับ RAM ด้วย RAM {ramGb:N0} GB คุณแทบไม่จำเป็นต้องใช้ การปิดจะคืนรอบการทำงานของ CPU ให้เกม"
                            : $"Memory compression แลกเวลา CPU กับ RAM ด้วย RAM {ramGb:N0} GB มันช่วยให้ไม่ต้องไปใช้ page file ที่ช้ากว่ามากเมื่อหน่วยความจำเต็ม จึงควรเปิดไว้"),
            ["mem_sysmain"] = ("ปิด SysMain (Superfetch)",
                "SysMain โหลดแอปเข้า RAM ไว้ล่วงหน้า บน SSD ส่วนใหญ่แค่ทำให้ดิสก์และ CPU ทำงานเบื้องหลัง มันยังเป็นตัวรัน memory compression ด้วย การปิดจึงปิด memory compression ไปด้วย "
                + (pc.SystemIsHDD ? "ตรวจพบ: Windows อยู่บนฮาร์ดดิสก์ ควรเปิด SysMain ไว้"
                   : !plentyOfRam ? $"ด้วย RAM {ramGb:N0} GB ควรเปิด SysMain ไว้เพื่อให้ memory compression ยังใช้ได้"
                   : pc.SystemIsSSD ? "ตรวจพบ: Windows อยู่บน SSD และมี RAM เหลือเฟือ แนะนำให้ปิด" : "ถ้า Windows อยู่บนฮาร์ดดิสก์ควรเปิดไว้")),
            ["mem_search"] = ("ปิด Search Indexing",
                "ตัวทำดัชนีไล่อ่านไฟล์ของคุณเบื้องหลัง ทำให้ฮาร์ดดิสก์ทำงานหนัก การค้นหาแอปใน Start menu ยังใช้ได้ แต่การค้นหาเนื้อหาในไฟล์จะช้าลง"),
            ["sto_lastaccess"] = ("ปิดการบันทึกเวลา Last-Access ของ NTFS",
                "ไม่ให้ NTFS เขียนเวลาทุกครั้งที่ไฟล์ถูกอ่าน เกมอ่านไฟล์เป็นพัน ๆ ไฟล์ ตัวนี้ตัดการเขียนดิสก์ส่วนเกินนั้นออก"),
            ["sto_8dot3"] = ("ปิดชื่อไฟล์แบบสั้น 8.3",
                "ไม่ให้ NTFS สร้างชื่อแบบ DOS รุ่นเก่า \"PROGRA~1\" ให้ทุกไฟล์ใหม่ ช่วยให้สร้างไฟล์เร็วขึ้นในโฟลเดอร์ที่มีไฟล์จำนวนมาก (การติดตั้งเกม, แคช shader)"),

            // ------------------------------------------------------------ NETWORK & PING
            ["net_nagle"] = ("ปิด Nagle's Algorithm",
                "ส่งแพ็กเก็ต TCP ขนาดเล็กทันทีแทนการรวมกลุ่ม ลดความหน่วงในเกมออนไลน์ที่ใช้ TCP (MMO, League, ล็อบบี้ของเกมยิงหลายเกม)"),
            ["net_throttle"] = ("ปิด Network Throttling",
                "Windows จำกัดทราฟฟิกเครือข่ายที่ไม่ใช่มัลติมีเดียขณะเล่นสื่อ การปิด NetworkThrottlingIndex จะเอาขีดจำกัดนั้นออก"),
            ["net_nicpower"] = ("ปิดการประหยัดพลังงานของการ์ดเครือข่าย",
                "ไม่ให้ Windows ปิดการ์ดเครือข่ายเพื่อประหยัดพลังงาน ซึ่งอาจทำให้แลคเป็นช่วง ๆ และหลุดการเชื่อมต่อชั่วครู่"
                + (laptop ? " เป็นตัวเลือกเสริมบนแล็ปท็อป เพราะทำให้แบตเตอรี่ตอนสแตนด์บายลดเร็วขึ้นเล็กน้อย" : "")),
            ["net_wifi"] = ("Wi-Fi ประสิทธิภาพสูงสุด",
                $"ไม่ให้การ์ดไร้สายงีบระหว่างแพ็กเก็ต (802.11 power save) ซึ่งทำให้ความหน่วงพุ่งแบบสุ่มบน Wi-Fi ไม่มีผลกับการเชื่อมต่อแบบสาย{ac}"),
            ["net_dosvc"] = ("หยุดการอัปโหลดอัปเดตแบบ Peer-to-Peer",
                "Delivery Optimization อัปโหลดอัปเดต Windows ไปให้เครื่องอื่นผ่านอินเทอร์เน็ตของคุณอยู่เบื้องหลัง ซึ่งทำให้ ping พุ่งได้ การดาวน์โหลดอัปเดตยังทำงานตามปกติ"),
            ["net_dns"] = ("ใช้ Cloudflare DNS (1.1.1.1)",
                "เปลี่ยนการ์ดเครือข่ายที่ใช้งานอยู่ไปใช้ DNS ที่เร็วของ Cloudflare ช่วยให้ค้นหาเซิร์ฟเวอร์และล็อกอิน launcher เร็วขึ้น ไม่ได้เปลี่ยน ping ในเกม การคืนค่าจะกลับไปใช้ DNS เดิมของคุณ"),

            // ------------------------------------------------------------ DEBLOAT
            ["dbl_copilot"] = ("ปิด Copilot",
                "ปิด Windows Copilot และซ่อนปุ่มบน taskbar เพื่อไม่ให้โหลดเบื้องหลัง"),
            ["dbl_widgets"] = ("ปิด Widgets / News & Interests",
                "เอาแผง Widgets (Windows 11) และ News & Interests (Windows 10) ออก ทั้งคู่รัน Edge WebView แบบซ่อนอยู่เบื้องหลัง ซึ่งใช้ RAM 100-300 MB และ CPU "
                + "Windows 11 build ใหม่ ๆ ล็อก policy นี้ไม่ให้เขียน ถ้า Windows ไม่อนุญาต ให้ปิด Widgets ที่ Settings > Personalization > Taskbar แทน"),
            ["dbl_bing"] = ("เอาผลค้นหาเว็บจาก Bing ออกจาก Start",
                "การค้นหาใน Start menu จะค้นเฉพาะในเครื่อง ผลลัพธ์เร็วขึ้นและไม่ส่งคำค้นไปยัง Bing ทุกครั้งที่พิมพ์"),
            ["dbl_autoinstall"] = ("บล็อกแอปสปอนเซอร์ที่ติดตั้งเอง",
                "ไม่ให้ Windows แอบติดตั้งแอปและเกมโปรโมต (Candy Crush, TikTok ฯลฯ) หลังอัปเดต"),
            ["dbl_startads"] = ("เอาคำแนะนำและทิปใน Start menu ออก",
                "เอาแอปโปรโมตใน Start, ป๊อปอัป \"tips & tricks\" และหน้าต้อนรับหลังอัปเดตออก"),
            ["dbl_settingsads"] = ("เอาโฆษณาใน Windows Settings ออก",
                "เอาคำแนะนำเชิงโปรโมตและโฆษณาออกจากแอป Settings ของ Windows"),
            ["dbl_lockads"] = ("เอาโฆษณาและ Fun Facts บน Lock Screen ออก",
                "เอา \"fun facts\" ทิป และโปรโมชันที่แสดงทับภาพ lock screen ออก"),
            ["dbl_recall"] = ("ปิด Recall Snapshots",
                "บล็อกฟีเจอร์ Windows Recall ไม่ให้จับภาพหน้าจอกิจกรรมของคุณ (ในเครื่องที่มีฟีเจอร์นี้)"),
            ["dbl_onedrive"] = ("ไม่ให้ OneDrive ทำงาน",
                "ไม่ให้ OneDrive เปิดและซิงก์เบื้องหลัง (ซึ่งอาจใช้ดิสก์และแบนด์วิดท์อัปโหลดจนเต็ม) ใช้เฉพาะเมื่อคุณไม่ได้พึ่ง OneDrive"),
            ["dbl_apps_news"] = ("ถอนแอป News, Weather, Maps และ Tips",
                "ถอน Microsoft News, Weather, Maps, Get Started/Tips, Get Help และ Feedback Hub ทุกตัวติดตั้งใหม่ได้จาก Microsoft Store"),
            ["dbl_apps_office"] = ("ถอน Office Hub, To Do, Clipchamp และ Teams",
                "ถอน Microsoft 365 hub, To Do, Power Automate, Clipchamp, Teams (ส่วนตัว), Skype, People และแอป Copilot ติดตั้งใหม่จาก Store ได้ถ้าต้องการ ข้ามตัวนี้ถ้าคุณใช้แอปใดในกลุ่มนี้"),
            ["dbl_apps_sponsored"] = ("ถอนเกมและแอปสปอนเซอร์",
                "ถอนของโปรโมตที่ติดมากับเครื่อง เช่น Candy Crush, Solitaire Collection, TikTok, Facebook, Instagram, Disney+ และ Prime Video เกมใน Steam / Xbox ของคุณจะไม่ถูกแตะ"),
            ["dbl_apps_legacy"] = ("ถอน Cortana, 3D Viewer และ Mixed Reality",
                "ถอน Cortana, 3D Viewer, Mixed Reality Portal และ Microsoft Wallet ของตกค้างที่แทบไม่มีใครใช้"),
            ["dbl_services"] = ("ปิดบริการที่ไม่ได้ใช้",
                "ปิด Fax, Retail Demo, Downloaded Maps Manager, Remote Registry, การแชร์เครือข่ายของ Windows Media Player และบริการ Insider ซึ่งไม่จำเป็นสำหรับเครื่องใช้งานที่บ้าน"),
            ["dbl_xbox"] = ("ปิดบริการ Xbox Live",
                "ปิดบริการยืนยันตัวตน cloud save และเครือข่ายของ Xbox Live ใช้เฉพาะเมื่อคุณไม่ได้ใช้แอป Xbox, Game Pass หรือเกมจาก Microsoft Store จอยยังใช้งานได้ตามปกติ"),
            ["dbl_spooler"] = ("ปิด Print Spooler",
                "ถ้าไม่มีเครื่องพิมพ์ Print Spooler ก็เปลือง RAM เปล่า ๆ และเป็นช่องทางโจมตีที่รู้จักกัน การพิมพ์ (รวมถึง \"Print to PDF\") จะใช้ไม่ได้จนกว่าจะคืนค่า"),

            // ------------------------------------------------------------ PRIVACY
            ["prv_telemetry"] = ("ลด Telemetry ของ Windows ให้ต่ำสุด",
                "ตั้งข้อมูลวินิจฉัยเป็นระดับต่ำสุดที่ Windows รุ่นของคุณอนุญาต และหยุดข้อความขอ feedback ลดการเก็บและอัปโหลดข้อมูลเบื้องหลัง"),
            ["prv_diagtrack"] = ("ปิดบริการ Telemetry (DiagTrack)",
                "ปิดบริการ Connected User Experiences and Telemetry และบริการ WAP push ที่ป้อนข้อมูลให้ ซึ่งตื่นขึ้นมาสแกนระบบและอัปโหลดข้อมูลเป็นระยะ"),
            ["prv_tasks"] = ("ปิด Scheduled Task ของ Telemetry",
                "ปิด Compatibility Appraiser, Customer Experience Improvement Program และ task วินิจฉัยดิสก์ โดยเฉพาะ Appraiser ที่ทำให้ CPU และดิสก์พุ่งได้นานหลายนาที บางครั้งกลางเกม"),
            ["prv_adid"] = ("ปิด Advertising ID",
                "ไม่ให้แอปใช้ advertising ID ติดตามคุณข้ามแอปเพื่อแสดงโฆษณาเฉพาะบุคคล"),
            ["prv_tailored"] = ("ปิด Tailored Experiences",
                "ไม่ให้ Microsoft ใช้ข้อมูลวินิจฉัยของคุณเพื่อแสดงทิป โฆษณา และคำแนะนำเฉพาะบุคคล"),
            ["prv_activity"] = ("ปิด Activity History",
                "ไม่ให้ Windows บันทึกและอัปโหลดไทม์ไลน์ของแอปและไฟล์ที่คุณเปิด"),
            ["prv_wer"] = ("ปิด Windows Error Reporting",
                "หยุดการเก็บและอัปโหลด crash dump หลังแอปแครช ซึ่งปกติจะกิน CPU และดิสก์ทันทีหลังเกมแครช"),
            ["prv_inking"] = ("ปิดการเก็บข้อมูลการพิมพ์และการเขียนด้วยปากกา",
                "ไม่ให้ Windows เก็บรูปแบบการพิมพ์ ลายมือ และรายชื่อติดต่อของคุณเพื่อ \"ปรับปรุง\" พจนานุกรมคีย์บอร์ด"),
            ["prv_remote"] = ("ปิด Remote Assistance",
                "ปิดฟีเจอร์เชิญ Remote Assistance ซึ่งเป็นช่องทางที่มิจฉาชีพแอบอ้างเป็นช่างชอบใช้ Remote Desktop และ Quick Assist ไม่ได้รับผลกระทบ"),
            ["prv_location"] = ("ปิด Location Services",
                "ปิดการเข้าถึงตำแหน่งของทุกแอป Weather, Maps และ \"Find my device\" จะไม่รู้ตำแหน่งของคุณอีก"),

            // ------------------------------------------------------------ QUALITY OF LIFE
            ["qol_stickykeys"] = ("ปิดป๊อปอัป Sticky / Filter / Toggle Keys",
                "กด Shift ห้าครั้งในเกมจะไม่ย่อเกมลงมาถามเรื่อง Sticky Keys อีก ฟีเจอร์ช่วยการเข้าถึงยังเปิดใช้ได้จาก Settings"),
            ["qol_endtask"] = ("\"End Task\" ในเมนูคลิกขวาของ Taskbar",
                "เพิ่ม End Task ในเมนูคลิกขวาของ taskbar เพื่อปิดเกมที่ค้างได้โดยไม่ต้องเปิด Task Manager"),
            ["qol_classicmenu"] = ("เมนูคลิกขวาแบบคลาสสิก",
                "นำเมนูคลิกขวาแบบเต็มสไตล์ Windows 10 กลับมา ซึ่งเปิดเร็วกว่าแบบใหม่ด้วย Explorer จะรีสตาร์ตเพื่อให้มีผล"),
            ["qol_fileext"] = ("แสดงนามสกุลไฟล์",
                "แสดง \".exe\", \".zip\" ฯลฯ ใน File Explorer มีประโยชน์ตอนลงม็อดเกมและช่วยสังเกตมัลแวร์ที่ปลอมตัวมา"),
            ["qol_thispc"] = ("เปิด File Explorer ที่ This PC",
                "File Explorer จะเปิดไปที่ไดรฟ์ของคุณทันทีแทน Home / Quick Access ซึ่งเร็วกว่าเมื่อมีไดรฟ์ขนาดใหญ่หลายตัว"),
            ["qol_verbose"] = ("แสดงสถานะการบูตและปิดเครื่องแบบละเอียด",
                "แสดงว่า Windows กำลังทำอะไรระหว่างเปิดและปิดเครื่อง แทนวงกลมหมุน ช่วยหาไดรเวอร์หรือบริการที่ช้า"),

            // ------------------------------------------------------------ ADVANCED
            ["adv_vbs"] = ("ปิด Memory Integrity (VBS / HVCI)",
                "Memory Integrity รัน kernel ภายใต้ hypervisor ซึ่งอาจกิน FPS 5-15% โดยเฉพาะบน CPU รุ่นเก่าที่ไม่มี MBEC (Intel ก่อน Gen 7, AMD ก่อน Zen 2) การปิดได้ประสิทธิภาพเพิ่มจริง แต่ลดการป้องกันมัลแวร์ระดับ kernel"
                + (pc.HvciOn ? " ขณะนี้กำลังทำงานอยู่บนเครื่องนี้" : " ขณะนี้ไม่ได้ทำงานบนเครื่องนี้")),
            ["adv_spectre"] = ("ปิด Mitigation ของ Spectre และ Meltdown",
                "CPU รุ่นเก่าเสียประสิทธิภาพมากจากแพตช์แก้บั๊ก CPU เหล่านี้ (system call, I/O ของดิสก์และเครือข่าย) การปิดอาจช่วย frame time ได้ชัดเจน แต่ทำให้เครื่องเสี่ยงต่อการโจมตีเหล่านั้น (เช่น ผ่านเว็บไซต์อันตราย) บน CPU รุ่นใหม่แทบไม่ได้ประโยชน์"),
        };

        foreach (var t in tweaks)
            if (th.TryGetValue(t.Id, out var text)) { t.NameTh = text.Name; t.DescTh = text.Desc; }
    }
}
