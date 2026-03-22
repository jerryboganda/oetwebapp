import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";

// Define the video item type
interface VideoItem {
  title: string;
  ratioClass: string;
  src: string;
  customStyle?: React.CSSProperties;
}

// Left column videos
const leftColumnVideos: VideoItem[] = [
  {
    title: "Ratio Video 1x1",
    ratioClass: "ratio ratio-1x1",
    src: "https://www.youtube.com/embed/BcKOz6kAgg0",
  },
  {
    title: "Ratio Video 4x3",
    ratioClass: "ratio ratio-4x3",
    src: "https://www.youtube.com/embed/BcKOz6kAgg0",
  },
];

// Right column videos
const rightColumnVideos: VideoItem[] = [
  {
    title: "Ratio Video 16x9",
    ratioClass: "ratio ratio-16x9",
    src: "https://www.youtube.com/embed/PIa17rsNSEE",
  },
  {
    title: "Custom ratios 50%",
    ratioClass: "ratio",
    customStyle: {
      "--bs-aspect-ratio": "50%",
    } as React.CSSProperties,
    src: "https://www.youtube.com/embed/EwzynNhx4Y8",
  },
  {
    title: "Ratio Video 21x9",
    ratioClass: "ratio ratio-21x9",
    src: "https://www.youtube.com/embed/Ep5kNwmDRlg",
  },
];

const VideoEmbedPage: React.FC = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Video Embed"
          title="Advance Ui"
          path={["Video Embed"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col md="6">
            {leftColumnVideos.map((video, index) => (
              <Card key={`left-${index}`}>
                <CardHeader>
                  <h5>{video.title}</h5>
                </CardHeader>
                <CardBody>
                  <div className={video.ratioClass} style={video.customStyle}>
                    <div>
                      <iframe
                        className="w-100 h-100"
                        src={video.src}
                        title={`YouTube video player ${index}`}
                        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                        allowFullScreen
                      ></iframe>
                    </div>
                  </div>
                </CardBody>
              </Card>
            ))}
          </Col>
          <Col md="6">
            {rightColumnVideos.map((video, index) => (
              <Card key={`right-${index}`}>
                <CardHeader>
                  <h5>{video.title}</h5>
                </CardHeader>
                <CardBody>
                  <div className={video.ratioClass} style={video.customStyle}>
                    <div>
                      <iframe
                        className="w-100 h-100"
                        src={video.src}
                        title={`YouTube video player ${index + leftColumnVideos.length}`}
                        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                        allowFullScreen
                      ></iframe>
                    </div>
                  </div>
                </CardBody>
              </Card>
            ))}
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default VideoEmbedPage;
