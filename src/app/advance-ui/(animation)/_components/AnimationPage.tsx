"use client";
import React, { useEffect, useRef, useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconBellRinging,
  IconBriefcase,
  IconChevronsRight,
  IconDownload,
  IconMessageCircle,
  IconTrash,
  IconUpload,
} from "@tabler/icons-react";
import AnimateUse from "@/app/advance-ui/(animation)/_components/AnimateUse";
import Link from "next/link";

type AnimationCategory = {
  id: string;
  name: string;
  animations: string[];
};

const AnimatedHover: React.FC<{
  animation: string;
  children: React.ReactNode;
}> = ({ animation, children }) => {
  const [isAnimating, setIsAnimating] = useState(false);

  const handleMouseEnter = () => {
    setIsAnimating(true);
  };

  const handleAnimationEnd = () => {
    setIsAnimating(false);
  };

  return (
    <Link
      href="#"
      onMouseEnter={handleMouseEnter}
      onAnimationEnd={handleAnimationEnd}
      className={`btn btn-light-primary ${isAnimating ? `animate__animated animate__${animation}` : ""}`}
    >
      {children}
    </Link>
  );
};

const AnimationPage = () => {
  const animationBlocksRef = useRef<HTMLDivElement>(null);
  const [openCategory, setOpenCategory] = useState<string | null>(null);
  const toggleCategory = (id: string) =>
    setOpenCategory((prev) => (prev === id ? null : id));

  useEffect(() => {
    if (typeof window === "undefined" || !animationBlocksRef.current) return;

    const initializeAnimationGrid = async () => {
      try {
        const [MasonryModule, Prism] = await Promise.all([
          import("masonry-layout"),
          import("prismjs"),
        ]);

        new MasonryModule.default(animationBlocksRef.current!, {
          percentPosition: true,
        });

        Prism.default.highlightAll();
      } catch {}
    };

    initializeAnimationGrid();
  }, []);

  const animationCategories: AnimationCategory[] = [
    {
      id: "attention-seekers",
      name: "Attention seekers",
      animations: [
        "bounce",
        "flash",
        "pulse",
        "rubberBand",
        "shakeX",
        "shakeY",
        "headShake",
        "swing",
        "tada",
        "wobble",
        "jello",
        "heartBeat",
      ],
    },
    {
      id: "back-entrances",
      name: "Back entrances",
      animations: ["backInDown", "backInLeft", "backInRight", "backInUp"],
    },
    {
      id: "back-exits",
      name: "Back exits",
      animations: ["backOutDown", "backOutLeft", "backOutRight", "backOutUp"],
    },
    {
      id: "bouncing-entrances",
      name: "Bouncing entrances",
      animations: [
        "bounceIn",
        "bounceInDown",
        "bounceInLeft",
        "bounceInRight",
        "bounceInUp",
      ],
    },
    {
      id: "bouncing-exits",
      name: "Bouncing exits",
      animations: [
        "bounceOut",
        "bounceOutDown",
        "bounceOutLeft",
        "bounceOutRight",
        "bounceOutUp",
      ],
    },
    {
      id: "fading-entrances",
      name: "Fading entrances",
      animations: [
        "fadeIn",
        "fadeInDown",
        "fadeInDownBig",
        "fadeInLeft",
        "fadeInLeftBig",
        "fadeInRight",
        "fadeInRightBig",
        "fadeInUp",
        "fadeInUpBig",
        "fadeInTopLeft",
        "fadeInTopRight",
        "fadeInBottomLeft",
        "fadeInBottomRight",
      ],
    },
    {
      id: "fading-exits",
      name: "Fading exits",
      animations: [
        "fadeOut",
        "fadeOutDown",
        "fadeOutDownBig",
        "fadeOutLeft",
        "fadeOutLeftBig",
        "fadeOutRight",
        "fadeOutRightBig",
        "fadeOutUp",
        "fadeOutUpBig",
        "fadeOutTopLeft",
        "fadeOutTopRight",
        "fadeOutBottomRight",
        "fadeOutBottomLeft",
      ],
    },
    {
      id: "flippers",
      name: "Flippers",
      animations: ["flip", "flipInX", "flipInY", "flipOutX", "flipOutY"],
    },
    {
      id: "lightspeed",
      name: "Lightspeed",
      animations: [
        "lightSpeedInRight",
        "lightSpeedInLeft",
        "lightSpeedOutRight",
        "lightSpeedOutLeft",
      ],
    },
    {
      id: "rotating-entrances",
      name: "Rotating entrances",
      animations: [
        "rotateIn",
        "rotateInDownLeft",
        "rotateInDownRight",
        "rotateInUpLeft",
        "rotateInUpRight",
      ],
    },
    {
      id: "rotating-exits",
      name: "Rotating exits",
      animations: [
        "rotateOut",
        "rotateOutDownLeft",
        "rotateOutDownRight",
        "rotateOutUpLeft",
        "rotateOutUpRight",
      ],
    },
    {
      id: "specials",
      name: "Specials",
      animations: ["hinge", "jackInTheBox", "rollIn", "rollOut"],
    },
    {
      id: "zooming-entrances",
      name: "Zooming entrances",
      animations: [
        "zoomIn",
        "zoomInDown",
        "zoomInLeft",
        "zoomInRight",
        "zoomInUp",
      ],
    },
    {
      id: "zooming-exits",
      name: "Zooming exits",
      animations: [
        "zoomOut",
        "zoomOutDown",
        "zoomOutLeft",
        "zoomOutRight",
        "zoomOutUp",
      ],
    },
    {
      id: "sliding-entrances",
      name: "Sliding entrances",
      animations: ["slideInDown", "slideInLeft", "slideInRight", "slideInUp"],
    },
    {
      id: "sliding-exits",
      name: "Sliding exits",
      animations: [
        "slideOutDown",
        "slideOutLeft",
        "slideOutRight",
        "slideOutUp",
      ],
    },
  ];

  return (
    <div className="animation-page">
      <Container fluid>
        <Breadcrumbs
          mainTitle="Animation"
          title="Advance Ui"
          path={["Animation"]}
          Icon={IconBriefcase}
        />

        <Row>
          <Col xs="12">
            <Card>
              <CardHeader>
                <h5>Where can use? some example ..!</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap gap-3">
                  <div className="h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-primary">
                    <img
                      src="/images/avatar/2.png"
                      alt="Pulse animation example"
                      className="img-fluid animate__pulse animate__animated animate__infinite animate__faster"
                    />
                  </div>
                  <span className="bg-secondary h-45 w-45 d-flex-center b-r-50 position-relative">
                    <img
                      src="/images/avatar/1.png"
                      alt="Zoom animation example"
                      className="img-fluid b-r-50"
                    />
                    <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle animate__animated animate__zoomIn animate__infinite animate__fast"></span>
                  </span>
                  <span className="bg-secondary h-45 w-45 d-flex-center b-r-50 position-relative">
                    <img
                      src="/images/avatar/6.png"
                      alt="Heartbeat animation example"
                      className="img-fluid b-r-50"
                    />
                    <span className="position-absolute top-10 start-40 translate-middle d-flex-center bg-danger border border-light rounded-circle text-center h-20 w-20 f-s-10">
                      <IconMessageCircle
                        size={10}
                        className="animate__animated animate__heartBeat animate__infinite animate__fast"
                      />
                    </span>
                  </span>
                  <span className="text-outline-primary h-45 w-45 d-flex-center b-r-50">
                    <IconBellRinging
                      size={24}
                      className="animate__animated animate__rubberBand animate__infinite animate__fast"
                    />
                  </span>
                  <Button type="button" className="btn btn-success btn-lg">
                    Submit
                    <IconChevronsRight
                      size={24}
                      className="animate__animated animate__fadeOutRight animate__infinite animate__fast"
                    />
                  </Button>
                  <Button type="button" className="btn btn-danger btn-lg">
                    <IconTrash
                      size={24}
                      className="animate__animated animate__bounceIn animate__infinite animate__fast"
                    />
                    Delete
                  </Button>
                  <Button type="button" className="btn btn-primary btn-lg">
                    <IconDownload
                      size={24}
                      className="animate__animated animate__bounceInDown animate__infinite animate__slow"
                    />
                    Download
                  </Button>
                  <Button type="button" className="btn btn-warning btn-lg">
                    Upload
                    <IconUpload
                      size={24}
                      className="animate__animated animate__fadeOutRight animate__infinite animate__fast"
                    />
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xs="12">
            <div ref={animationBlocksRef} className="animation-blocks">
              {animationCategories.map((category) => (
                <Card
                  key={category.id}
                  className="cheatsheet-card animation-card"
                >
                  <div className="card-header p-0">
                    <Link
                      href={`#${category.id}`}
                      className="btn btn-primary w-100 text-center f-s-18 f-w-500 rounded-bottom-0 py-2"
                      onClick={() => toggleCategory(category.id)}
                      aria-expanded={openCategory === category.id}
                    >
                      {category.name}
                    </Link>
                  </div>
                  <div className="collapse card-body show" id={category.id}>
                    <ul>
                      <li>
                        <div className="d-flex flex-wrap gap-3">
                          {category.animations.map((animation) => (
                            <AnimatedHover
                              key={`${category.id}-${animation}`}
                              animation={animation}
                            >
                              {animation}
                            </AnimatedHover>
                          ))}
                        </div>
                      </li>
                    </ul>
                  </div>
                </Card>
              ))}
            </div>
          </Col>

          <AnimateUse />
        </Row>
      </Container>
    </div>
  );
};

export default AnimationPage;
