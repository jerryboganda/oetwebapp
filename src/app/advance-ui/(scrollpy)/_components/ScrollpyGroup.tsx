import React, { useRef, useState } from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";

const ScrollpyGroup: React.FC = () => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [activeId, setActiveId] = useState("list-item-1");
  const itemRefs = useRef<(HTMLHeadingElement | null)[]>([]);

  React.useEffect(() => {
    itemRefs.current = itemRefs.current.slice(0, 4);
  }, []);

  const handleScroll = () => {
    if (!scrollRef.current) return;

    const scrollTop = scrollRef.current.scrollTop;

    for (let i = 0; i < itemRefs.current.length; i++) {
      const item = itemRefs.current[i];
      if (!item) continue;

      const offsetTop = item.offsetTop;
      const offsetHeight = item.offsetHeight;

      if (
        scrollTop + 50 >= offsetTop &&
        scrollTop + 50 < offsetTop + offsetHeight
      ) {
        setActiveId(item.id);
        break;
      }
    }
  };

  const addItemRef = (el: HTMLHeadingElement | null, index: number) => {
    itemRefs.current[index] = el;
  };

  return (
    <Col xs="12">
      <Row>
        <Col sm={4}>
          <Card>
            <CardHeader>
              <h5>Scrollpy with list-group</h5>
            </CardHeader>
            <CardBody>
              <Row>
                <div id="list-example" className="list-group">
                  <a
                    className={`list-group-item nav-pill-primary list-group-item-action ${
                      activeId === "list-item-1" ? "active" : ""
                    }`}
                    href="#list-item-1"
                  >
                    Item 1
                  </a>
                  <a
                    className={`list-group-item nav-pill-primary list-group-item-action ${
                      activeId === "list-item-2" ? "active" : ""
                    }`}
                    href="#list-item-2"
                  >
                    Item 2
                  </a>
                  <a
                    className={`list-group-item nav-pill-primary list-group-item-action ${
                      activeId === "list-item-3" ? "active" : ""
                    }`}
                    href="#list-item-3"
                  >
                    Item 3
                  </a>
                  <a
                    className={`list-group-item nav-pill-primary list-group-item-action ${
                      activeId === "list-item-4" ? "active" : ""
                    }`}
                    href="#list-item-4"
                  >
                    Item 4
                  </a>
                </div>
              </Row>
            </CardBody>
          </Card>
        </Col>
        <div className="col-sm-8">
          <Card>
            <CardBody>
              <div
                ref={scrollRef}
                onScroll={handleScroll}
                className="scrollspy-example h-215 overflow-y-scroll app-scroll"
              >
                <h5
                  id="list-item-1"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 0)}
                >
                  Item 1
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  A digital artisan, skilled in the art of crafting captivating
                  online experiences, is what one would refer to as a web
                  designer. This master of the virtual realm possesses an innate
                  ability to blend aesthetics with functionality, creating
                  visually stunning websites that leave a lasting impression on
                  the beholder. With a keen eye for detail and an unwavering
                  commitment to perfection, the web designer meticulously weaves
                  together colors, typography, and imagery to construct a
                  virtual masterpiece that not only captivates the senses but
                  also effortlessly guides the user through a seamless digital
                  journey.
                </p>

                <h5
                  id="list-item-2"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 1)}
                >
                  Item 2
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  Picture a modern-day Leonardo da Vinci, armed with a palette
                  of pixels and a canvas of code, and you have the essence of a
                  web designer. This visionary artist possesses an unparalleled
                  understanding of the digital landscape, effortlessly
                  transforming abstract concepts into tangible online realities.
                  With an unwavering dedication to staying ahead of the
                  ever-evolving trends, the web designer is a true pioneer,
                  constantly pushing the boundaries of creativity and
                  innovation. Their work is a testament to their ability to
                  harmonize technology and design, resulting in websites that
                  are not only visually striking but also functionally flawless.
                </p>

                <h5
                  id="list-item-3"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 2)}
                >
                  Item 3
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  {" "}
                  In the realm of the digital realm, the web designer reigns
                  supreme as the virtuoso of the online universe. Armed with an
                  arsenal of technical skills and an insatiable thirst for
                  creativity, this digital maestro orchestrates a symphony of
                  pixels and lines of code to create awe-inspiring websites that
                  are as visually captivating as they are user-friendly. With an
                  acute understanding of user experience and an unwavering
                  commitment to excellence, the web designer meticulously crafts
                  every element of a website, ensuring that it not only reflects
                  the brand&#39;s identity but also engages and delights its
                  visitors.{" "}
                </p>

                <h5
                  id="list-item-4"
                  className="f-w-500 mb-2 text-dark"
                  ref={(el) => addItemRef(el, 3)}
                >
                  Item 4
                </h5>
                <p className="f-s-15 text-secondary mg-b-14">
                  {" "}
                  A digital artisan, skilled in the art of crafting captivating
                  online experiences, is what one would refer to as a web
                  designer. This master of the virtual realm possesses an innate
                  ability to blend aesthetics with functionality, creating
                  visually stunning websites that leave a lasting impression on
                  the beholder. With a keen eye for detail and an unwavering
                  commitment to perfection, the web designer meticulously weaves
                  together colors, typography, and imagery to construct a
                  virtual masterpiece that not only captivates the senses but
                  also effortlessly guides the user through a seamless digital
                  journey.
                </p>
              </div>
            </CardBody>
          </Card>
        </div>
      </Row>
    </Col>
  );
};
export default ScrollpyGroup;
