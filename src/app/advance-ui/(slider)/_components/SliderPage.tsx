"use client";
import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Slider from "react-slick";
import { IconBriefcase } from "@tabler/icons-react";

const SliderPage = () => {
  const multipleItems = {
    infinite: true,
    slidesToShow: 2,
    slidesToScroll: 1,
    responsive: [
      {
        breakpoint: 991,
        settings: {
          slidesToShow: 3,
        },
      },
      {
        breakpoint: 567,
        settings: {
          slidesToShow: 1,
          slidesToScroll: 1,
        },
      },
    ],
  };
  const centerItems = {
    centerMode: true,
    slidesToShow: 2,
    responsive: [
      {
        breakpoint: 768,
        settings: {
          centerMode: true,
          slidesToShow: 2,
        },
      },
      {
        breakpoint: 567,
        settings: {
          arrows: false,
          centerMode: true,
          slidesToShow: 1,
        },
      },
    ],
  };
  const responsiveItems = {
    dots: true,
    infinite: false,
    speed: 300,
    slidesToShow: 2,
    slidesToScroll: 4,
    responsive: [
      {
        breakpoint: 991,
        settings: {
          slidesToShow: 3,
        },
      },
      {
        breakpoint: 576,
        settings: {
          slidesToShow: 1,
        },
      },
    ],
  };
  const variableItems = {
    dots: true,
    infinite: true,
    speed: 300,
    slidesToShow: 1,
    centerMode: true,
    variableWidth: true,
  };

  // const sliderRef = useRef<Slider>(null); // Reference to the Slider
  const [slides, setSlides] = useState<number[]>([1]);

  // Slider settings
  const addRemoveSettings = {
    dots: true,
    infinite: slides.length > 1,
    speed: 500,
    slidesToShow: Math.min(slides.length, 4),
    slidesToScroll: Math.min(slides.length, 4),
    vertical: false,
    responsive: [
      {
        breakpoint: 768,
        settings: {
          slidesToShow: Math.min(slides.length, 3),
        },
      },
      {
        breakpoint: 576,
        settings: {
          slidesToShow: 1,
        },
      },
    ],
  };

  // Add slide handler
  const handleAddSlide = () => {
    setSlides((prev) => [...prev, prev.length + 1]); // Add new slide index
  };
  const handleRemoveSlide = () => {
    setSlides((prev) => (prev.length > 1 ? prev.slice(0, -1) : prev)); // Remove last slide
  };

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Slider"
        title="Advance Ui"
        path={["Slider"]}
        Icon={IconBriefcase}
      />
      <Row className="app-slick-sliders">
        <Col lg="6">
          <Card>
            <CardHeader>
              <h5>Multiple Items</h5>
            </CardHeader>
            <CardBody>
              <Slider className="multiple-items app-arrow" {...multipleItems}>
                <div className="items">
                  <img
                    src="/images/slick/09.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
                <div className="items">
                  <img
                    src="/images/slick/23.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
                <div className="items">
                  <img
                    src="/images/slick/25.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
                <div className="items">
                  <img
                    src="/images/slick/24.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
                <div className="items">
                  <img
                    src="/images/slick/26.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
                <div className="items">
                  <img
                    src="/images/slick/27.jpg"
                    alt="image"
                    className="img-fluid rounded"
                  />
                </div>
              </Slider>
            </CardBody>
          </Card>
        </Col>
        <Col lg="6">
          <Card className="equal-card">
            <CardHeader>
              <h5>Center Mode</h5>
            </CardHeader>
            <CardBody>
              <Slider className="center-mode app-arrow" {...centerItems}>
                <div className="item">
                  <img
                    src="/images/slick/04.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="item">
                  <img
                    src="/images/slick/03.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="item">
                  <img
                    src="/images/slick/04.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="item">
                  <img
                    src="/images/slick/05.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="item">
                  <img
                    src="/images/slick/06.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="item">
                  <img
                    src="/images/slick/07.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
              </Slider>
            </CardBody>
          </Card>
        </Col>
        <Col lg="6">
          <Card className="equal-card">
            <CardHeader>
              <h5>Responsive Display</h5>
            </CardHeader>
            <CardBody>
              <Slider className="responsive app-arrow" {...responsiveItems}>
                <div className="resopns-item">
                  <img
                    src="/images/slick/10.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="resopns-item">
                  <img
                    src="/images/slick/23.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="resopns-item">
                  <img
                    src="/images/slick/05.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="resopns-item">
                  <img
                    src="/images/slick/25.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="resopns-item">
                  <img
                    src="/images/slick/06.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
                <div className="resopns-item">
                  <img
                    src="/images/slick/03.jpg"
                    className="img-fluid rounded"
                    alt="image"
                  />
                </div>
              </Slider>
            </CardBody>
          </Card>
        </Col>
        <Col lg="6">
          <Card className="py-3">
            <CardHeader>
              <h5>Variable Width</h5>
            </CardHeader>
            <CardBody>
              <Slider className="variable-width app-arrow" {...variableItems}>
                <div className="slider_width_1"></div>
                <div className="slider_width_2"></div>
                <div className="slider_width_3"></div>
                <div className="slider_width_4"></div>
                <div className="slider_width_5"></div>
                <div className="slider_width_6"></div>
              </Slider>
            </CardBody>
          </Card>
        </Col>
        <Col lg="6">
          <div className="card">
            <div className="card-header">
              <h5>Add & Remove</h5>
            </div>
            <div className="card-body">
              <Slider
                // ref={sliderRef}
                className="slider add-remove app-arrow"
                {...addRemoveSettings}
              >
                {slides.map((slide, index) => (
                  <div key={index} className="p-2">
                    <img
                      src={`/images/slick/12.jpg`}
                      alt={`Slide ${slide}`}
                      className="img-fluid rounded"
                    />
                  </div>
                ))}
              </Slider>

              <div className="text-center add-remove-btn mt-4">
                <button
                  className="button js-add-slide btn btn-light-primary"
                  onClick={handleAddSlide}
                >
                  Add Slide
                </button>
                <button
                  className="button js-remove-slide btn btn-light-danger"
                  onClick={handleRemoveSlide}
                >
                  Remove Slide
                </button>
              </div>
            </div>
          </div>
        </Col>
      </Row>
    </Container>
  );
};

export default SliderPage;
