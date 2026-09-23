/**
 * 富文本消息模型（对齐 C# RichMessageContent）。
 * 服务端 IM 消息的 Content 字段为此结构的 JSON 序列化。
 * Schema: horizon-rich-message-v1
 */
/** 消息类型枚举（对齐 C# MessageType） */
export class MessageType {
    static readonly Text: number = 0;
    static readonly Image: number = 1;
    static readonly Video: number = 2;
    static readonly LinkCard: number = 3;
    static readonly File: number = 4;
    static readonly System: number = 5;
    static readonly Activity: number = 6;
    static readonly News: number = 7;
    static readonly Emoji: number = 8;
}
/** 消息附件 */
export class MessageAttachment {
    mediaUrl: string = '';
    previewImageUrl: string = '';
    attachmentType: string = 'image';
}
/** 富文本消息内容 */
export class RichMessageContent {
    static readonly SchemaName: string = 'horizon-rich-message-v1';
    schema: string = RichMessageContent.SchemaName;
    type: number = MessageType.Text;
    text: string = '';
    mediaUrl: string = '';
    previewImageUrl: string = '';
    playbackUrl: string = '';
    originalUrl: string = '';
    title: string = '';
    subtitle: string = '';
    siteName: string = '';
    provider: string = '';
    supportsInlinePlayback: boolean = false;
    emojiId: number = -1;
    fileName: string = '';
    fileSize: number = 0;
    attachments: MessageAttachment[] = [];
    /** 创建文本消息 */
    static createText(text: string): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.Text;
        c.text = text != null ? text : '';
        return c;
    }
    /** 创建图片消息 */
    static createImage(mediaUrl: string, text: string = '', title: string = '', subtitle: string = '', previewImageUrl: string = ''): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.Image;
        c.text = text != null ? text : '';
        c.mediaUrl = mediaUrl != null ? mediaUrl : '';
        c.originalUrl = mediaUrl != null ? mediaUrl : '';
        c.previewImageUrl = previewImageUrl && previewImageUrl.length > 0 ? previewImageUrl : (mediaUrl != null ? mediaUrl : '');
        c.title = title != null ? title : '';
        c.subtitle = subtitle != null ? subtitle : '';
        c.siteName = '图片';
        c.provider = 'image';
        c.supportsInlinePlayback = false;
        return c;
    }
    /** 创建视频消息 */
    static createVideo(mediaUrl: string, playbackUrl: string, text: string = '', title: string = '', subtitle: string = '', previewImageUrl: string = '', siteName: string = '视频', provider: string = 'video'): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.Video;
        c.text = text != null ? text : '';
        c.mediaUrl = mediaUrl != null ? mediaUrl : '';
        c.originalUrl = mediaUrl != null ? mediaUrl : '';
        c.previewImageUrl = previewImageUrl != null ? previewImageUrl : '';
        c.playbackUrl = playbackUrl != null ? playbackUrl : '';
        c.title = title != null ? title : '';
        c.subtitle = subtitle != null ? subtitle : '';
        c.siteName = siteName != null ? siteName : '';
        c.provider = provider != null ? provider : '';
        c.supportsInlinePlayback = playbackUrl != null && playbackUrl.length > 0;
        return c;
    }
    /** 创建链接卡片消息 */
    static createLinkCard(originalUrl: string, playbackUrl: string, title: string, subtitle: string, siteName: string, previewImageUrl: string = '', text: string = '', provider: string = 'link', supportsInlinePlayback: boolean = false): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.LinkCard;
        c.text = text != null ? text : '';
        c.originalUrl = originalUrl != null ? originalUrl : '';
        c.mediaUrl = originalUrl != null ? originalUrl : '';
        c.previewImageUrl = previewImageUrl != null ? previewImageUrl : '';
        c.playbackUrl = playbackUrl != null ? playbackUrl : '';
        c.title = title != null ? title : '';
        c.subtitle = subtitle != null ? subtitle : '';
        c.siteName = siteName != null ? siteName : '';
        c.provider = provider != null ? provider : '';
        c.supportsInlinePlayback = supportsInlinePlayback && playbackUrl != null && playbackUrl.length > 0;
        return c;
    }
    /** 创建文件消息 */
    static createFile(fileName: string, text: string = '', fileSize: number = 0, mediaUrl: string = ''): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.File;
        c.text = text && text.length > 0 ? text : fileName;
        c.mediaUrl = mediaUrl != null ? mediaUrl : '';
        c.fileName = fileName != null ? fileName : '未知文件';
        c.fileSize = fileSize;
        c.provider = 'file';
        return c;
    }
    /** 创建表情消息 */
    static createEmoji(emojiId: number, emojiText: string = ''): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.Emoji;
        c.text = emojiText != null ? emojiText : '';
        c.emojiId = emojiId;
        c.provider = 'emoji';
        return c;
    }
    /**
     * 创建语音消息（信封 IMContentType.Audio=2）。
     * 富文本结构复用 Video（MessageType.Video=2 与信封 Audio=2 数值一致，
     * 序列化/反序列化类型不会被改写；PC 端按视频卡片降级渲染，鸿蒙端按 provider='audio' 渲染语音卡片）。
     * subtitle 存时长秒数（字符串），渲染端显示 "N\""。
     */
    static createAudio(mediaUrl: string, durationSeconds: number): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.Video;
        c.mediaUrl = mediaUrl;
        c.originalUrl = mediaUrl;
        c.playbackUrl = mediaUrl;
        c.subtitle = `${Math.max(1, Math.round(durationSeconds))}`;
        c.siteName = '语音';
        c.provider = 'audio';
        c.supportsInlinePlayback = true;
        return c;
    }
    /** 是否为语音消息（Video 结构 + provider=audio） */
    get isAudio(): boolean {
        return this.provider === 'audio';
    }
    /** 解析语音时长（秒）；非语音消息返回 0 */
    getAudioDuration(): number {
        const n = Number(this.subtitle);
        return Number.isFinite(n) && n > 0 ? Math.round(n) : 0;
    }
    /**
     * 创建位置消息（信封 IMContentType.Location=5）。
     * 富文本结构复用 LinkCard（跨端兼容：PC 端按链接卡片降级渲染），
     * OriginalUrl 存 geo:lat,lng 供地图解析；provider='location' 供鸿蒙端渲染位置卡片。
     */
    static createLocation(lat: number, lng: number, title: string, address: string = ''): RichMessageContent {
        const c = new RichMessageContent();
        c.type = MessageType.LinkCard;
        c.title = title.length > 0 ? title : '位置';
        c.subtitle = address;
        c.originalUrl = `geo:${lat},${lng}`;
        c.mediaUrl = c.originalUrl;
        c.siteName = '位置';
        c.provider = 'location';
        c.supportsInlinePlayback = false;
        return c;
    }
    /** 是否为位置消息（LinkCard 结构 + provider=location） */
    get isLocation(): boolean {
        return this.provider === 'location';
    }
    /** 解析 geo:lat,lng → [lat, lng]；非位置消息返回 null */
    getLocation(): number[] | null {
        if (this.originalUrl.startsWith('geo:')) {
            const parts = this.originalUrl.slice(4).split(',');
            if (parts.length === 2) {
                const lat = Number(parts[0]);
                const lng = Number(parts[1]);
                if (Number.isFinite(lat) && Number.isFinite(lng)) {
                    return [lat, lng];
                }
            }
        }
        return null;
    }
    /** 获取显示文本（从 Text 字段提取，用于聊天列表等简洁显示） */
    getDisplayText(): string {
        if (this.isLocation) {
            return this.title && this.title.length > 0 ? `[位置] ${this.title}` : '[位置]';
        }
        if (this.isAudio) {
            const d = this.getAudioDuration();
            return d > 0 ? `[语音] ${d}"` : '[语音]';
        }
        if (this.text && this.text.length > 0) {
            return this.text;
        }
        switch (this.type) {
            case MessageType.Image:
                return '[图片]';
            case MessageType.Video:
                return '[视频]';
            case MessageType.LinkCard:
                return this.title && this.title.length > 0 ? `[链接] ${this.title}` : '[链接]';
            case MessageType.File:
                return `[文件] ${this.fileName}`;
            case MessageType.Emoji:
                return this.text && this.text.length > 0 ? this.text : '[表情]';
            default:
                return '[未知消息]';
        }
    }
    /** 是否有文本内容 */
    get hasText(): boolean {
        return this.text !== null && this.text.length > 0;
    }
    /** 是否为卡片类消息（图片/视频/链接卡片） */
    get hasCard(): boolean {
        return this.type === MessageType.Image ||
            this.type === MessageType.Video ||
            this.type === MessageType.LinkCard;
    }
    /** 是否为文件消息 */
    get isFile(): boolean {
        return this.type === MessageType.File;
    }
    /** 是否为表情消息 */
    get isEmoji(): boolean {
        return this.type === MessageType.Emoji;
    }
}
/**
 * 附件序列化接口
 */
class AttachmentDto {
    MediaUrl: string = '';
    PreviewImageUrl: string = '';
    AttachmentType: string = '';
}
/**
 * 富文本消息序列化器（对齐 C# RichMessageContentSerializer）。
 * 负责解析服务端 IM 消息的 Content 字段（JSON 格式）。
 */
export class RichMessageContentSerializer {
    /** 序列化 RichMessageContent 为 JSON 字符串 */
    static serialize(content: RichMessageContent): string {
        const c = content != null ? content : RichMessageContent.createText('');
        const attachments: AttachmentDto[] = [];
        for (let i = 0; i < c.attachments.length; i++) {
            const a = c.attachments[i];
            const dto = new AttachmentDto();
            dto.MediaUrl = a.mediaUrl;
            dto.PreviewImageUrl = a.previewImageUrl;
            dto.AttachmentType = a.attachmentType;
            attachments.push(dto);
        }
        const obj: Record<string, Object> = {
            'Schema': c.schema,
            'Type': c.type,
            'Text': c.text,
            'MediaUrl': c.mediaUrl,
            'PreviewImageUrl': c.previewImageUrl,
            'PlaybackUrl': c.playbackUrl,
            'OriginalUrl': c.originalUrl,
            'Title': c.title,
            'Subtitle': c.subtitle,
            'SiteName': c.siteName,
            'Provider': c.provider,
            'SupportsInlinePlayback': c.supportsInlinePlayback,
            'EmojiId': c.emojiId,
            'FileName': c.fileName,
            'FileSize': c.fileSize,
            'Attachments': attachments
        };
        return JSON.stringify(obj);
    }
    /**
     * 信封 IMContentType（服务端消息外层类型）→ 富文本 MessageType 映射。
     * 两套枚举编号不一致（如 IMContentType.Emoji=7 而 MessageType.Emoji=8），必须转换后再参与判定。
     */
    static mapEnvelopeContentType(contentType: number): number {
        switch (contentType) {
            case 1: // IMContentType.Image
                return MessageType.Image;
            case 2: // IMContentType.Audio（富文本内为 Video 结构 + provider='audio'；两者数值均为 2）
                return MessageType.Video;
            case 3: // IMContentType.Video
                return MessageType.Video;
            case 4: // IMContentType.File
                return MessageType.File;
            case 5: // IMContentType.Location（富文本内为 LinkCard + provider=location）
                return MessageType.LinkCard;
            case 7: // IMContentType.Emoji
                return MessageType.Emoji;
            default:
                return MessageType.Text;
        }
    }

    /** 反序列化：从 JSON 字符串解析 RichMessageContent */
    static deserialize(contentType: number, rawContent: string): RichMessageContent {
        // 信封类型先映射为富文本类型，保证 Emoji/Video 等编号错位时判定正确
        const mappedType = RichMessageContentSerializer.mapEnvelopeContentType(contentType);
        if (rawContent === null || rawContent.length === 0) {
            return RichMessageContentSerializer.createLegacyFallback(mappedType, '');
        }
        try {
            const parsed = JSON.parse(rawContent) as Record<string, Object>;
            const schema = (parsed['Schema'] as string) ?? '';
            if (schema === RichMessageContent.SchemaName) {
                const content = new RichMessageContent();
                content.schema = schema;
                content.type = (parsed['Type'] as number) ?? contentType;
                content.text = (parsed['Text'] as string) ?? '';
                content.mediaUrl = (parsed['MediaUrl'] as string) ?? '';
                content.previewImageUrl = (parsed['PreviewImageUrl'] as string) ?? '';
                content.playbackUrl = (parsed['PlaybackUrl'] as string) ?? '';
                content.originalUrl = (parsed['OriginalUrl'] as string) ?? '';
                content.title = (parsed['Title'] as string) ?? '';
                content.subtitle = (parsed['Subtitle'] as string) ?? '';
                content.siteName = (parsed['SiteName'] as string) ?? '';
                content.provider = (parsed['Provider'] as string) ?? '';
                content.supportsInlinePlayback = (parsed['SupportsInlinePlayback'] as boolean) ?? false;
                content.emojiId = (parsed['EmojiId'] as number) ?? -1;
                content.fileName = (parsed['FileName'] as string) ?? '';
                content.fileSize = (parsed['FileSize'] as number) ?? 0;
                // 解析附件
                const attachmentsRaw = parsed['Attachments'];
                if (Array.isArray(attachmentsRaw)) {
                    const arr = attachmentsRaw as Array<Record<string, Object>>;
                    for (let i = 0; i < arr.length; i++) {
                        const a = arr[i];
                        const att = new MessageAttachment();
                        att.mediaUrl = (a['MediaUrl'] as string) ?? '';
                        att.previewImageUrl = (a['PreviewImageUrl'] as string) ?? '';
                        att.attachmentType = (a['AttachmentType'] as string) ?? 'image';
                        content.attachments.push(att);
                    }
                }
                // 规范化：确保类型与 contentType 一致
                // 例外：位置消息（信封 5）复用 LinkCard 结构，保留 JSON 内类型避免被改写
                if (content.type !== contentType && contentType !== MessageType.Text && contentType !== 5) {
                    content.type = contentType;
                }
                return RichMessageContentSerializer.normalize(content);
            }
        }
        catch (_e) {
            // JSON 解析失败，走旧格式回退
        }
        return RichMessageContentSerializer.createLegacyFallback(contentType, rawContent);
    }
    /** 规范化内容（确保空值安全） */
    private static normalize(content: RichMessageContent): RichMessageContent {
        content.text = content.text ?? '';
        content.mediaUrl = content.mediaUrl ?? '';
        content.originalUrl = content.originalUrl ?? '';
        content.previewImageUrl = content.previewImageUrl ?? '';
        content.playbackUrl = content.playbackUrl ?? '';
        content.title = content.title ?? '';
        content.subtitle = content.subtitle ?? '';
        content.siteName = content.siteName ?? '';
        content.provider = content.provider ?? '';
        content.attachments = content.attachments ?? [];
        content.schema = RichMessageContent.SchemaName;
        return content;
    }
    /** 旧格式回退（非 JSON 或 Schema 不匹配时） */
    private static createLegacyFallback(type: number, rawContent: string): RichMessageContent {
        switch (type) {
            case MessageType.Image:
                return RichMessageContent.createImage(rawContent, '', '图片消息');
            case MessageType.Video:
                return RichMessageContent.createVideo(rawContent, rawContent, '', '视频消息', '直链视频');
            case MessageType.LinkCard:
                return RichMessageContent.createLinkCard(rawContent, rawContent, rawContent, '', '链接卡片');
            case MessageType.Emoji:
                return RichMessageContent.createEmoji(-1, rawContent);
            default:
                return RichMessageContent.createText(rawContent);
        }
    }
}
