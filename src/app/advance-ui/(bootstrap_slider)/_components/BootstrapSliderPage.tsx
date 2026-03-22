"use client";
import React, { useState, useEffect } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import { IconBriefcase } from "@tabler/icons-react";

// Define TypeScript interfaces
interface CarouselProps {
  images: string[];
  showIndicators?: boolean;
  showControls?: boolean;
  autoPlay?: boolean;
  interval?: number;
  showCaptions?: boolean;
  captions?: string[];
}

// Reusable Carousel Component
const ReactCarousel: React.FC<CarouselProps> = ({
  images,
  showIndicators = false,
  showControls = false,
  autoPlay = false,
  interval = 3000,
  showCaptions = false,
  captions = [],
}) => {
  const [currentSlide, setCurrentSlide] = useState<number>(0);

  // Auto-play functionality
  useEffect(() => {
    if (!autoPlay) return;

    const timer = setInterval(() => {
      setCurrentSlide((prev) => (prev + 1) % images.length);
    }, interval);

    return () => clearInterval(timer);
  }, [autoPlay, interval, images.length]);

  const goToSlide = (index: number): void => {
    setCurrentSlide(index);
  };

  const goToPrev = (): void => {
    setCurrentSlide((prev) => (prev === 0 ? images.length - 1 : prev - 1));
  };

  const goToNext = (): void => {
    setCurrentSlide((prev) => (prev === images.length - 1 ? 0 : prev + 1));
  };

  return (
    <div className="carousel slide">
      {/* Indicators */}
      {showIndicators && (
        <div className="carousel-indicators">
          {images.map((_: string, index: number) => (
            <button
              key={index}
              type="button"
              className={index === currentSlide ? "active" : ""}
              onClick={() => goToSlide(index)}
              aria-label={`Slide ${index + 1}`}
            />
          ))}
        </div>
      )}

      {/* Carousel Inner */}
      <div className="carousel-inner">
        {images.map((image: string, index: number) => (
          <div
            key={index}
            className={`carousel-item ${index === currentSlide ? "active" : ""}`}
          >
            <img
              src={image}
              className="w-100 d-block"
              alt={`Slide ${index + 1}`}
            />
            {showCaptions && captions[index] && (
              <div className="carousel-caption d-none d-md-block">
                <p>{captions[index]}</p>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Controls */}
      {showControls && (
        <>
          <button
            className="carousel-control-prev"
            type="button"
            onClick={goToPrev}
          >
            <span className="carousel-control-prev-icon" aria-hidden="true" />
            <span className="visually-hidden">Previous</span>
          </button>
          <button
            className="carousel-control-next"
            type="button"
            onClick={goToNext}
          >
            <span className="carousel-control-next-icon" aria-hidden="true" />
            <span className="visually-hidden">Next</span>
          </button>
        </>
      )}
    </div>
  );
};

const BootstrapSliderPage: React.FC = () => {
  // Common images array
  const sliderImages: string[] = [
    "/images/bootstrapslider/01.jpg",
    "/images/bootstrapslider/07.jpg",
    "/images/bootstrapslider/08.jpg",
  ];

  const captionedImages: string[] = [
    "/images/bootstrapslider/03.jpg",
    "/images/bootstrapslider/07.jpg",
    "/images/bootstrapslider/08.jpg",
  ];

  const captions: string[] = [
    "Some representative placeholder content for the first slide.",
    "Some representative placeholder content for the second slide.",
    "Some representative placeholder content for the third slide.",
  ];

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="React Carousel"
          title="Advance Ui"
          path={["React Carousel"]}
          Icon={IconBriefcase}
        />

        <Row>
          {/* Simple Slider */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">Simple Slider</h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel images={sliderImages} showControls={true} />
              </CardBody>
            </Card>
          </Col>

          {/* Indicator Slider */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">Indicator Slider</h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel
                  images={[
                    "/images/bootstrapslider/02.jpg",
                    "/images/bootstrapslider/07.jpg",
                    "/images/bootstrapslider/08.jpg",
                  ]}
                  showIndicators={true}
                  showControls={true}
                />
              </CardBody>
            </Card>
          </Col>

          {/* Slider With Captions */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">Slider With Captions</h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel
                  images={captionedImages}
                  showIndicators={true}
                  showCaptions={true}
                  captions={captions}
                />
              </CardBody>
            </Card>
          </Col>

          {/* Auto Slider */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">Auto Slider</h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel
                  images={[
                    "/images/bootstrapslider/04.jpg",
                    "/images/bootstrapslider/07.jpg",
                    "/images/bootstrapslider/08.jpg",
                  ]}
                  autoPlay={true}
                  interval={3000}
                />
              </CardBody>
            </Card>
          </Col>

          {/* Auto Slider With Indicators */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">
                  Auto Slider With Indicators
                </h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel
                  images={[
                    "/images/bootstrapslider/05.jpg",
                    "/images/bootstrapslider/07.jpg",
                    "/images/bootstrapslider/08.jpg",
                  ]}
                  showIndicators={true}
                  autoPlay={true}
                  interval={2000}
                />
              </CardBody>
            </Card>
          </Col>

          {/* Auto Slider With Buttons */}
          <Col md={6}>
            <Card>
              <CardHeader>
                <h5 className="text-center mt-2">Auto Slider With Buttons</h5>
              </CardHeader>
              <CardBody>
                <ReactCarousel
                  images={[
                    "/images/bootstrapslider/06.jpg",
                    "/images/bootstrapslider/07.jpg",
                    "/images/bootstrapslider/08.jpg",
                  ]}
                  showControls={true}
                  autoPlay={true}
                  interval={3000}
                />
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default BootstrapSliderPage;
