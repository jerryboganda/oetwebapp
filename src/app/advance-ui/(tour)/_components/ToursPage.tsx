"use client";
import React, { useEffect } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Shepherd from "shepherd.js";
import Slider from "react-slick";
import "shepherd.js/dist/css/shepherd.css";

import {
  IconBrandGithub,
  IconBriefcase,
  IconCake,
  IconDeviceLaptop,
  IconMail,
  IconMapPin,
  IconPhone,
  IconPhotoHeart,
  IconUserCheck,
} from "@tabler/icons-react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faPlayCircle } from "@fortawesome/free-solid-svg-icons";
import TourTab from "@/app/advance-ui/(tour)/_components/TourTab";
import {
  extraImages,
  featuredStories,
  mainImage,
  multiLinkImage,
  socialStats,
  subImages,
  videos,
} from "@/Data/AdvanceUi/Tour/tourData";

const aboutItems = [
  {
    icon: <IconBriefcase size={18} />,
    label: "Work passion",
    value: "IT Section",
  },
  {
    icon: <IconMail size={18} />,
    label: "Email",
    value: "Ninfa@gmail.com",
  },
  {
    icon: <IconPhone size={18} />,
    label: "Contact",
    value: "0364 4559103",
  },
  {
    icon: <IconCake size={18} />,
    label: "Birth of Date",
    value: "24 Oct",
  },
  {
    icon: <IconMapPin size={18} />,
    label: "Location",
    value: "Via Partenope, 117",
  },
  {
    icon: <IconDeviceLaptop size={18} />,
    label: "Website",
    value: "Ninfa_devWWW.com",
  },
  {
    icon: <IconBrandGithub size={18} />,
    label: "Github",
    value: "Ninfa_dev",
  },
];

const ToursPage = () => {
  useEffect(() => {
    const tour = new Shepherd.Tour({
      useModalOverlay: true,
      defaultStepOptions: {
        cancelIcon: {
          enabled: true,
        },
        classes: "shepherd-theme-custom",
        scrollTo: {
          behavior: "smooth",
          block: "center",
        },
      },
    });

    tour.addStep({
      id: "profile-tabs",
      title: "All Tabs!",
      text: "Go and check Now 👍",
      attachTo: {
        element: "#profile-tabs",
        on: "bottom",
      },
      buttons: [
        { text: "< Back", action: tour.back },
        { text: "Next >", action: tour.next },
      ],
    });

    tour.addStep({
      id: "featured-Stories",
      title: "Stories!",
      text: "Beautiful day starts with some pictures.",
      attachTo: {
        element: "#featured-Stories",
        on: "bottom",
      },
      buttons: [
        { text: "< Back", action: tour.back },
        { text: "Next >", action: tour.next },
      ],
    });

    tour.addStep({
      id: "post",
      title: "Post",
      text: " Some picture of our post secthion..\n",
      attachTo: {
        element: "#post",
        on: "bottom",
      },
      buttons: [
        { text: "< Back", action: tour.back },
        { text: "Next >", action: tour.next },
      ],
    });

    tour.addStep({
      id: "about-me",
      title: "About Me",
      text: " something details about me!!\n",
      attachTo: {
        element: "#about-me",
        on: "bottom",
      },
      buttons: [
        { text: "< Back", action: tour.back },
        { text: "Next >", action: tour.next },
      ],
    });

    tour.addStep({
      id: "friend",
      title: "Friend",
      text: " Friendlists who follow this!\n",
      attachTo: {
        element: "#friend",
        on: "bottom",
      },
      buttons: [
        { text: "< Back", action: tour.back },
        { text: "Done &#x1F44D;", action: tour.cancel },
      ],
    });

    const timer = setTimeout(() => {
      tour.start();
    }, 300);

    return () => {
      clearTimeout(timer);
      tour.cancel();
    };
  }, []);

  const settingsProps = {
    slidesToShow: 4,
    slidesToScroll: 1,
    autoplay: true,
    arrows: false,
    autoplaySpeed: 1000,
    responsive: [
      {
        breakpoint: 1366,
        settings: {
          slidesToShow: 2,
        },
      },
      {
        breakpoint: 768,
        settings: {
          slidesToShow: 3,
        },
      },
      {
        breakpoint: 480,
        settings: {
          slidesToShow: 1,
        },
      },
    ],
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Tour"
          title="Advance Ui"
          path={["Tour"]}
          Icon={IconBriefcase}
        />
        <Row>
          <TourTab />
          <Col lg="5" xxl="6" className="col-box-5">
            <div className="content-wrapper">
              <div id="tab-1" className="tabs-content active">
                <div className="profile-content">
                  <Card id="featured-Stories">
                    <CardHeader>
                      <h5>Featured Stories</h5>
                    </CardHeader>
                    <CardBody>
                      <Slider
                        className="story-container app-arrow"
                        {...settingsProps}
                      >
                        {featuredStories.map((story, index) => (
                          <div key={index}>
                            <div className="story">
                              <img
                                src={story.profileImage}
                                className="rounded img-fluid"
                                alt={`story-${index}`}
                              />
                              <div
                                className={`h-45 w-45 d-flex-center b-r-50 overflow-hidden story-icon ${story.bg}`}
                              >
                                <img
                                  src={story.avatar}
                                  alt={`avatar-${index}`}
                                  className="img-fluid"
                                />
                              </div>
                            </div>
                          </div>
                        ))}
                      </Slider>
                    </CardBody>
                  </Card>
                  <Card id="post">
                    <CardHeader>
                      <h5>Post</h5>
                    </CardHeader>
                    <CardBody>
                      <Col xs="12">
                        <div className="photos-container">
                          <div className="left-main-img img-box">
                            <a
                              href={mainImage.href}
                              className="glightbox"
                              data-glightbox="type: image; zoomable: true;"
                            >
                              <img src={mainImage.src} alt="image" />
                              <div className="transparent-box2">
                                <div className="captions">
                                  {mainImage.caption}
                                </div>
                              </div>
                            </a>
                          </div>
                          <div>
                            <div className="sub">
                              {subImages.map((src, index) => (
                                <div key={index} className="img-box">
                                  <a
                                    href={src}
                                    className="glightbox"
                                    data-glightbox="type: image"
                                  >
                                    <img src={src} alt="image" />
                                    <div className="transparent-box2">
                                      <div className="captions">
                                        Simple image example
                                      </div>
                                    </div>
                                  </a>
                                </div>
                              ))}
                              <div id="multi-link" className="img-box">
                                <a
                                  href={multiLinkImage.href}
                                  className="glightbox"
                                  data-glightbox="type: image"
                                >
                                  <img src={multiLinkImage.src} alt="image" />
                                  <div className="transparent-box">
                                    <div className="caption">
                                      {multiLinkImage.caption}
                                    </div>
                                  </div>
                                </a>
                              </div>
                            </div>
                          </div>
                          <div
                            id="more-img"
                            className="extra-images-container hide-element"
                          >
                            {extraImages.map((src, index) => (
                              <a
                                key={index}
                                href={src}
                                className="glightbox"
                                data-glightbox="type: image"
                              >
                                <img src={src} alt="image" />
                              </a>
                            ))}
                          </div>
                        </div>

                        {/* Video Gallery */}
                        <div className="photos-container">
                          <div className="left-main-img img-box">
                            <a
                              href="/images/profile/video.mp4"
                              className="glightbox"
                            >
                              <img src={videos[0]} alt="image" />
                              <div className="transparent-box">
                                <div className="caption">
                                  <FontAwesomeIcon
                                    icon={faPlayCircle}
                                    size="lg"
                                  />
                                </div>
                              </div>
                            </a>
                          </div>
                          <div className="right-main-img img-box">
                            <a
                              href="/images/profile/video.mp4"
                              className="glightbox"
                            >
                              <img src={videos[1]} alt="image" />
                              <div className="transparent-box">
                                <div className="caption">
                                  <FontAwesomeIcon
                                    icon={faPlayCircle}
                                    size="lg"
                                  />
                                </div>
                              </div>
                            </a>
                          </div>
                        </div>
                      </Col>
                    </CardBody>
                  </Card>
                </div>
              </div>
            </div>
          </Col>
          <Col lg="4" xxl="3" className="order--1-lg col-box-4">
            <Card>
              <CardBody>
                <div className="profile-container">
                  <div className="image-details">
                    <div className="profile-image"></div>
                    <div className="profile-pic">
                      <div className="avatar-upload">
                        <div className="avatar-edit">
                          <input
                            type="file"
                            id="imageUpload"
                            accept=".png, .jpg, .jpeg"
                          />
                          <label htmlFor="imageUpload">
                            <IconPhotoHeart size={24} />
                          </label>
                        </div>
                        <div className="avatar-preview">
                          <div id="imgPreview"></div>
                        </div>
                      </div>
                    </div>
                  </div>
                  <div className="person-details">
                    <h5 className="f-w-600">
                      Ninfa Monaldo
                      <img
                        width="20"
                        height="20"
                        src="/images/profile/01.png"
                        alt="instagram-check-mark"
                      />
                    </h5>
                    <p>Web designer &amp; Developer</p>
                    <div className="details">
                      {socialStats.map((stat, index) => (
                        <div key={index}>
                          <h4 className="text-primary">{stat.value}</h4>
                          <p className="text-secondary">{stat.label}</p>
                        </div>
                      ))}
                    </div>
                    <div className="my-2">
                      <button type="button" className="btn btn-primary b-r-22">
                        <IconUserCheck size={18} />
                        Follow
                      </button>
                    </div>
                  </div>
                </div>
              </CardBody>
            </Card>
            <Card id="about-me">
              <CardHeader>
                <h5>About Me</h5>
              </CardHeader>
              <CardBody>
                <p className="text-muted f-s-13">
                  Hello! I am, <strong>Ninfa Monaldo</strong> Devoted web
                  designer with over five years of experience and a strong
                  understanding of Adobe Creative Suite, HTML5, CSS3 and Java.
                  Excited to bring my exceptional front-end development
                  abilities to the retail industry.
                </p>
                <div className="about-list">
                  {aboutItems.map((item, index) => (
                    <div key={index}>
                      <span className="fw-medium">
                        {item.icon} {item.label}
                      </span>
                      <span className="float-end f-s-13 text-secondary">
                        {item.value}
                      </span>
                    </div>
                  ))}
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ToursPage;
